# UnicodeRegEx — Working Notes

Scratchpad for in-progress design decisions and next steps. Not user documentation.

## Guiding principle

The CLI is **not** meant to recreate grep. grep guides the *engine's* capabilities: the engine should
expose everything needed for someone to build grep on top of it. Classify every feature:

- **Capability** → engine (`SearchRequest` config, or the callback contract).
- **Presentation / composition** → tool (line splitting, counting, formatting, combining patterns with `|`).

This came out of a full option-by-option pass over GNU grep. Most grep options are presentation or
pattern-expressible; the real engine gaps cluster into: bidirectional callbacks, richer per-hit data,
match flags, and ordered filters.

## Decisions (settled)

### Callbacks become bidirectional (was: pure observer)
- Callbacks **return a response** so the tool can control flow (`void` → response).
- **`OnHit`** returns Continue / StopFile / StopAll.
  - StopFile stops the current file's enumeration only (→ grep `-m N`).
  - StopAll stops the whole job (redundant with `job.Cancel()` but far more convenient — keep both).
  - **Fires in apply (rewrite) mode too**, not just search: each match is reported *before* its
    replacement is written. In apply mode a stop response **abandons the current file** — we bail out of
    the segment loop without committing, so the delete-on-close temp is discarded and the original is
    left byte-for-byte intact (StopFile → next file, StopAll → cancel job). Consequence: a hit can be
    reported and then abandoned, so "was applied" keys off `OnFileChanged`, never `OnHit`.
- **`OnFile`** also returns a response and can:
  - StopFile / StopAll (before any matching begins).
  - **Override the code page** the file is decoded with (this *replaces* the old idea of a request-level
    "search-with-specific-encoding": the tool has the file, the detected page, and the bytes in hand).
    Ordering: `OnFile` fires **after** detection + the binary gate; the override feeds the `RegExInput`
    that is then built.
- **Stop swallowing callback exceptions.** A thrown sink exception is a bug → **fault the job**
  (distinct from the deliberate StopFile/StopAll responses). Under parallel processing the throw happens on
  a worker thread; it is wrapped as an internal `SinkException`, surfaced by `Parallel.ForEach` inside an
  `AggregateException`, and unwrapped back to the original exception so the job faults identically to the
  serial path.
- **`OnError(path, Exception)` carries the exception, not a pre-baked string.** Every error the engine
  reports *has* an exception (missing path, access failure, per-file fault), so the sink gets it and
  decides how to present/classify it. The CLI turns `FileNotFound`/`DirectoryNotFound` into grep's
  "no such file or directory" itself. (This retired the one message that had *no* exception behind it —
  see binary edges below.)
- `job.Cancel()` stays; intentionally **not** immediate (checked at file/enumeration boundaries, not
  mid-file). Acceptable. (A prompt mid-file cancel is possible by unmapping under the engine → SEH,
  which needs `/EHa` to avoid a small boost leak; judged not worthwhile now.)

### Per-hit / per-file content (the "SearchHit context" work)
- **`RegExPinnedBytes` is a ref struct**, so it can't live on `SearchFile` (a class). Therefore
  **`OnFile` receives file content as a separate `RegExPinnedBytes` parameter**, not via `SearchFile`.
  (Built: the `OnFile` content-bytes parameter is **deferred** — the hit already carries `Match.Input`,
  so add it only when a consumer needs bytes at `OnFile` time.)
- **`SearchHit` becomes a `ref struct`** and carries an **entire `RegExMatch`** (itself a ref struct):
  gives the tool submatches, offsets, `Format()`, `CopyInput`, etc. `Text`/`Replacement` become derived
  (lazy) rather than eagerly materialized → a count-only tool (`-c`) pays nothing for strings.
  Unlocks grep `-A/-B/-C` (context), `-n` (line numbers), `-o` (matched slice), and the precise
  `-a/-I` "NUL in unmatched segments" check — all tool-side.
- **Lifetime contract (critical):** a `RegExMatch` is valid **only during the single `OnHit` call** —
  the enumerator mutates a shared underlying COM object on each `MoveNext`, so a stored match goes stale
  on the next iteration (tighter than "valid for the file"). `OnFile`'s content bytes are valid only
  while that file is mapped. Rule: **copy anything you need to keep, during the callback.**

### Filtering
- Engine gets an **ordered list of filename filters**, each `{ Include | Exclude, glob }`.
- **Directory filters are a SEPARATE list** (exclude-dir) — relative ordering between filename filters
  and directory filters is meaningless (different names), so they do not interleave.
- Current single `Include` string becomes one entry in the filename-filter list.
- **Ordering lives in the request model (an ordered list), NOT the settings system** — sidesteps the
  "settings are last-wins" limitation. The parser appends entries in encounter order (a small, contained
  parser capability), like building a positional list.
- **CLI does not need to expose ordered include/exclude now** (low priority; mainly a *testing* aid). If
  ever exposed, prefer a single-string syntax with a prefix marking exclude, e.g. `~foo.cs;*.cs`, rather
  than multiple options.

#### Exact filename-filter rule (from the grep docs — order IS significant)
grep: *"If contradictory --include and --exclude options are given, the last matching one wins. If no
--include or --exclude options match, a file is included unless the first such option is --include."*
Algorithm, per file, over the ordered filename filters:
```
verdict = null
foreach f in filters (in order):
    if f.Glob matches Path.GetFileName(file):
        verdict = (f.Kind == Include)     // last matching wins → overwrite
if verdict != null: return verdict
return filters.Count == 0 || filters[0].Kind != Include   // "first is Include" ⇒ default-exclude
```
Locked-in facts:
- Match against **`Path.GetFileName`** (not the path).
- **Applies only during recursive traversal**; explicitly-named input files bypass filters (already true).
- The **no-match default is decided by the FIRST filter's kind**, not a global mode. Surprising case:
  `--exclude=*.tmp --include=*.cs` still *includes* `foo.txt` (nothing matched; first is Exclude).
  Precompute `defaultInclude = filters.Count == 0 || filters[0].Kind != Include`.
- **Directory filters are EXCLUDE-ONLY** (grep has `--exclude-dir`, no `--include-dir` — an include-dir
  would make nested dirs unreachable). They *prune the walk*: skip descending into a directory whose name
  matches any exclude-dir glob. No ordering rule needed (exclude-only), hence the separate list.

### Directories & links
- **`DirectoryDisposition` (replaced the `Recurse` bool)** governs every directory encountered (a search
  target or a discovered subdirectory), applied *after* the directory-filter verdict:
  `Error` (default; reports an `IOException("Is a directory")` via `OnError`, grep `-d read`), `Skip`,
  `ReadImmediateFiles` (files, no descent), `RecurseNoLinks`, `RecurseWithLinks`. Error/Skip never recurse,
  and only Recurse* push subdirectories, so a discovered directory only ever exists under a Recurse* run.
- **Roots are NOT special** (matches grep 3.11): directory filters apply to command-line directories too,
  and a directory arg with no recurse flag hits `Error`. CLI `-r` → `RecurseNoLinks`; no `-r` → `Error`.
- **Link handling:** the directory walk enumerates with `NativeDir` (a `FindFirstFileEx`
  wrapper, `Engine\NativeDir.cs`), which yields lightweight `NativeDirEntry` values carrying
  each child's name, attributes, and reparse **tag** (`dwReserved0`) straight from the directory scan.
  `RecurseNoLinks` skips subdirectories that are *name-surrogate* reparse points (real links);
  `RecurseWithLinks` follows them, with identity-based cycle prevention (see the Cycle prevention bullet).
- **Reparse classification is by tag (DONE — was a coarse-check limitation).** The old check used
  `FileAttributes.ReparsePoint`, which is set for *every* reparse point, so `RecurseNoLinks` over-skipped
  non-link reparse points: OneDrive/cloud placeholders (`IO_REPARSE_TAG_CLOUD*`), Data Dedup stubs
  (`_DEDUP`), ProjFS/VFS-for-Git working trees (`_PROJFS`), container isolation (`_WCI*`). Now
  `NativeDirEntry.IsNameSurrogate` classifies by tag via `IsReparseTagNameSurrogate` (the `0x20000000` bit;
  future-proof vs. a tag blocklist), so only real links (symlink/junction/mount point) are skipped and
  non-link reparse points are walked like ordinary directories. The tag rides in `WIN32_FIND_DATA.dwReserved0`
  for free with the `FindFirstFileEx` enumerator (the BCL does not surface it through `FileSystemInfo`).
- **`NativeDir` notes.** `WIN32_FIND_DATA` uses blittable `fixed char` buffers (not
  `[MarshalAs(ByValTStr)]` strings), so out-marshalling is a raw copy and no name string is allocated per
  entry; the name is built on demand only for entries the walk keeps. `Enumerate(dir, skipDirectories)`
  gates directory entries on `dwFileAttributes` *before* the name is materialized (the non-recursing path
  passes `skipDirectories: true`, matching the old `EnumerateFiles` fast path that never turned skipped
  entries into objects). Paths are extended-length (`\\?\`) prefixed for MAX_PATH parity; `.`/`..` are
  filtered from the fixed buffer without allocating. The walk stack is now `Stack<string>` (directory
  paths); child paths are composed with `Path.Combine` on demand. `SearchJobTests` (48 tests, incl.
  `RecurseNoLinks_SkipsJunctionedDirectory`, `RecurseWithLinks_FollowedJunction_IsDeduplicatedByIdentity`,
  `RecurseWithLinks_CycleIsBroken`) validates the walk.
- **Cycle prevention (DONE).** Only relevant when following links (NTFS/ReFS loop only through a directory
  reparse point; hardlinked dirs aren't allowed), so only `RecurseWithLinks` uses it. It is **identity-based,
  not path-based**. The BCL exposes no directory identity on netstandard2.0/net48, so `NativeDir`
  `TryGetDirectoryId` does it via P/Invoke: `CreateFile(path, 0, FILE_SHARE_*, OPEN_EXISTING,
  FILE_FLAG_BACKUP_SEMANTICS)` (backup semantics = open a *directory* handle) then
  `GetFileInformationByHandleEx(FileIdInfo)` → `FILE_ID_INFO` (volume serial + 128-bit file id, ReFS-correct;
  on NTFS the high 64 bits are 0). `DirectoryId` bundles those into an equatable/hashable value.
  - **Policy = global visited-set (option A), chosen deliberately over ancestor-only (option B).** The walk
    records every descended directory's `DirectoryId` in one `HashSet`; a directory whose id was already
    seen is not descended again. This cuts cycles AND de-duplicates a directory reached via more than one
    link (a diamond), so the same directory's contents are never searched twice and no file's matches are
    reported twice. Option B (track only root→current ancestors; follow non-cycle re-visits) is the
    grep/`find -L` behavior but would double-report; for a *search* tool, dedup is the better default. The
    test `RecurseWithLinks_FollowedJunction_IsDeduplicatedByIdentity` pins A (a followed junction to `real`
    yields 1 hit, not 2); `RecurseWithLinks_CycleIsBroken` pins termination.
  - The root's id is seeded so a link straight back to the root is caught. A root is walked even if its id
    can't be read (user named it explicitly). A discovered directory whose id probe **fails** is skipped and
    reported (`IOException`) -- a followed directory we can't identify is never descended, so an
    unresolvable link can't hang the walk. One `CreateFile` per descended directory, only under
    `RecurseWithLinks` (an opt-in mode); zero cost otherwise. **Independent of reparse-tag classification** --
    different disposition, handle-based (not enumeration-based), keyed on identity (not tag).
- **Special files (`-D`): OUT OF SCOPE, but now consistently an error.** Devices / FIFOs / pipes /
  sockets are structurally unsupported: the engine mmaps a real file, so there is nothing to stream from
  — and the library's point is in-place *replace* of files, which a stream can't be. `ProcessFile` does a
  pre-open `GetFileType` check and reports anything that is not `FILE_TYPE_DISK` as an
  `IOException("Not a regular file")` via `OnError` (counts toward `Summary.Errors`) — so a special file
  is never silently mistaken for a successfully-searched empty file. A `-D` option would only toggle
  Error-vs-Skip (log verbosity, not capability), and we've chosen consistent-Error, so it stays won't-do.
  (grep's `-D` matters only because grep can *read* a FIFO.)
- **Zero-length regular files ARE searched/replaced** (bug fixed). An empty file can't be memory-mapped
  (`CreateFromFile` throws), so the empty case feeds the regex a plain **null / zero-length** input
  (`new RegExPinnedBytes()`, i.e. `{ data = 0, size = 0 }`). The native layer now accepts this: the
  root-cause bug was in `MakeCodePointRangeAndPos`, whose `nullptr` "out-of-range" pos sentinel collided
  with a *valid empty span* (whose begin is also `nullptr`), so `MatchEnumerator` threw `E_INVALIDARG`.
  Fixed by adding a `bool posValid` field to `CodePointRangeAndPos` (an empty span at offset 0 is now
  `posValid == true`) and changing `MatchEnumerator` to throw only when `!posValid`. So `^`/`a*`/`^$`
  match the single empty position and empty replacements run. `MatchFile`/`ApplyReplaceFile` now share one
  `ProcessFile` helper (open → GetFileType → detect → OnFile → verb body via a `FileProcessor` delegate,
  since `RegExInput` is a ref struct and can't be a `Func<>` arg).

### Line terminators / binary edges
- **Binary handling is a fact + one convenience bool, not a policy enum.** Detection reports the fact
  (`SearchFile.LooksBinary`); the engine applies a single mechanical gate driven by
  `SearchRequest.SkipBinaryFiles` (default **true**, consistent with `Include`/`Recurse` as "common
  policy as config"). Set it false to search binary files anyway (still reported as `LooksBinary`, so a
  sink wanting other handling can act from `OnFile`). The old `BinaryFileDisposition { Skip/Error/Search }`
  is **gone**: its `Error` case was a smell — a "binary file" error with **no exception** behind it,
  wrong channel — and binary files no longer count toward `Summary.Errors`.
- `-U`/`--binary` (no CR stripping): already the behavior (engine is byte-oriented, never strips CR).
- `-z`/`--null-data`: tool-side line-splitting policy (split on `\0` vs `\n`); `RegExLineCounter` could
  take a configurable terminator. No byte mutation.
- `-w`, `-x`, whole-line: expressed in the pattern (`\<`/`\>`, `^`/`$`); the *tuning* of that behavior
  is boost match/syntax flags → covered by the match-flags work. **RESOLVED (`$`-vs-`\r\n`):** with the
  perl group, whether `^`/`$` see embedded newlines is the `no_mod_m` bit (Boost defaults m-**on**),
  exposed as `MultilineAnchors` (default true). And whether `.` crosses a newline is `mod_s`/`no_mod_s`,
  exposed as authoritative `DotAll` (default off → `no_mod_s`). No engine help beyond these toggles needed.

### Composition / presentation (tool-side; engine does nothing)
- `-e`, `-f` (multiple / from-file patterns): combine with `|` in the tool.
- `-v`, `-c`, `-o`, `-n`, `-L`, `-l`, `-s -b -H -h --label -T -Z`: presentation. `-L`/`-l`/`-c` rely on
  `OnFile` (now load-bearing).
- `-q`: `job.Cancel()` or a StopAll response.

## To-do (sequencing)

Done and captured in **Decisions (settled)** above + the status snapshot: SearchHit context, bidirectional
callback responses, syntax + match-flag settings (both `SyntaxFlags` and `MatchFlags` masks), the
**ordered filename + directory filter lists** (`SearchRequest.FileNameFilters` / `DirectoryFilters`, both
`List<GlobFilter>`; `AddIncludeFileGlobs` / `AddExcludeDirGlobs` translate a semicolon string;
`GlobFilterSet` collapses contiguous same-kind runs into `(Kind, Regex)` segments and applies
last-match-wins), and **`DirectoryDisposition`** (replaced the `Recurse` bool; Error(default)/Skip/
ReadImmediateFiles/RecurseNoLinks/RecurseWithLinks). Directory filters use the **same include/exclude
rules** but pass `defaultIncludeWhenNoMatch: true` (Option B). Filters apply **uniformly to roots and
discovered dirs** (roots not special — matches grep 3.11); the disposition then decides Error/Skip/read/
recurse. `RecurseNoLinks` skips *name-surrogate* reparse-point subdirs; `RecurseWithLinks` follows them
with identity-based cycle prevention (visited-set of directory ids; option A -- dedups diamonds too). The
walk enumerates with a `FindFirstFileEx`-based `NativeDir` (reparse-tag classification done). CLI `-r` →
`RecurseNoLinks`, no `-r` → `Error`; still `--include`-only otherwise.

The directory-walk engine work is complete (filters, dispositions, reparse-tag classification, and cycle
prevention all done). Remaining is CLI surfacing only:

(Special-file `-D` disposition is **out of scope** — see Directories & links above.)

Also still open (CLI surfacing; engine side is done):
- **Syntax/match-flag settings on the CLI** — the `SyntaxFlags`/`MatchFlags` masks and the
  `ComposeSyntaxFlags`/`SetSyntaxFlags` options (ignoreCase/collate/dotAll/freeSpacing/multilineAnchors)
  exist on the request but are **not** on the CLI yet (by design); surface later as GUI checkboxes /
  advanced CLI options. The `Syntax` flavor choice + `-i` are already wired via
  `ChoiceSetting`/`CommandLineBinding` and folded in by `ApplySettings` (`-E`/`-G`/`-P`/`-F`, `-i`).
- CLI exposure of detection toggles + `--binary-files`/`SkipBinaryFiles` (advanced options / GUI checkboxes).

## Open questions (to decide)

- **`OnFile` code-page override (deferred):** would let the tool re-decode a file after seeing it, replacing
  a request-level "search-with-specific-encoding". Tangles with `SearchFile` immutability/identity (it is
  built before `OnFile`; an override needs a rebuilt instance for the hits). Likely shape: a small
  `SearchFileResponse` struct (action + `int?` codePageOverride) instead of the bare `SearchResponse` enum.
- **`OnDirectory` callback vs directory-disposition config:** a per-directory callback is flexible but
  awkward (synchronous per-directory decisions), and config has covered every need so far. Prefer config
  (recurse + exclude-dir + follow-links, plus a "report directory" option for `-d read`) unless a concrete
  need survives. (Enumeration is single-threaded, so a callback wouldn't face worker-thread races -- but
  config is still preferred on simplicity grounds.)
- **Manual/computed replacement (deferred):** letting the tool supply its own replacement bytes for a
  match (rather than the engine's `Format()` template). Lean: a **lambda** on the request/apply path,
  NOT a return value from `OnHit`. Revisit only if a real need appears.

## Status snapshot

- Engine is `SearchJob` (async / cancel / progress / dispose; two-pass enumerate→process; sink
  serialized; one-shot). Phase 2 (file processing) parallelizes across files via
  `SearchRequest.MaxDegreeOfParallelism` (1 = serial default; 0 = auto = `ProcessorCount`; >1 = capped).
  The compiled `RegEx` is shared across workers (immutable + free-threaded via the native FTM; each
  `Match`/`Replace` builds a fresh `MatchEnumerator`), and each file's work is independent (own handle /
  mmap / enumerator / replacement temp file), so the only shared state is the sink (serialized by
  `sinkGate`), the `CancellationTokenSource`, and interlocked counters. A throwing sink faults the job with
  the ORIGINAL exception on both paths (serial rethrow; parallel unwraps `AggregateException` →
  `SinkException` → inner). Enumeration (Phase 1) stays single-threaded (fast; parallelizing it would
  complicate the cycle-prevention visited-set for little gain).
  - **Perf benchmark**: `SearchJobPerfTests` (`[TestCategory("Perf")]`, excluded from the normal run --
    filter `TestCategory!=Perf`). Synthetic corpus (consts; fixed seed), sweeps DOP {1,2,4,8,auto},
    asserts only correctness (identical match count + 0 errors per DOP) and **reports** wall-clock /
    files-s / MB-s / speedup via `TestContext`. Self-guards to host-native: uses `IsWow64Process2`'s
    nativeMachine (NOT `RuntimeInformation.OSArchitecture`, which is masked under emulation) and marks
    itself `Inconclusive` if the process arch != true host. Run it with the **arm64-native** runner
    (`vstest.console.arm64.exe`); the x64 `vstest.console.exe` runs emulated and self-skips. Baseline
    (arm64, 20 procs, Debug native): DOP 1\u21924.01x at auto (2.11x @2, 3.07x @4, 3.68x @8) -- threading
    confirmed working.
- Encoding/binary detection: `EncodingDetector`, ordered per-step-toggleable heuristics (BOM, UTF-16
  NUL-parity, strict UTF-8, binary NUL + control-ratio); `EncodingDetectionOptions` + a `SkipBinaryFiles`
  bool on `SearchRequest`; per-file results via `SearchFile` + `ISearchSink.OnFile`.
- Callbacks: `OnFile`/`OnHit` return `SearchResponse`; `OnFileComplete` (closes the `OnFile` bracket --
  fires once per file `OnFile` accepted, after its last hit, not for skipped/errored/empty files -- so a
  sink can group a file's output under parallelism); `OnFileChanged`; `OnError(path, Exception)`.
  `SearchHit` is a `readonly ref struct { SearchFile File; RegExMatch Match; }` (+ lazy `Text`/`Replacement`).
  `SearchSinkBase` is an opt-in adapter (defaults steering callbacks to `Continue`, notifications to no-op)
  for consumers that override only what they need; deliberately NOT used by first-party production sinks
  so an `ISearchSink` change stays a compile error there (the base would silently no-op new callbacks --
  documented tradeoff).
- Syntax + match flags: `SearchRequest.SyntaxFlags` / `.MatchFlags` (single raw masks), validated by C++
  allow-masks; `ComposeSyntaxFlags`/`SetSyntaxFlags` helpers. Not on the CLI yet (see to-do).
- Filters: request holds ordered `FileNameFilters` and `DirectoryFilters` lists (`List<GlobFilter>`);
  `GlobFilterSet` applies last-match-wins, collapsing same-kind runs into one regex each. Filenames use
  grep's default (include unless first filter is an include); directories force default-include (Option B).
  Filters apply to roots and discovered dirs alike (roots not special). Named files bypass. CLI still
  exposes only `--include` (→ all-Include filters); CLI `--exclude` / `--exclude-dir` not yet wired.
- Directories: `DirectoryDisposition` (Error default / Skip / ReadImmediateFiles / RecurseNoLinks /
  RecurseWithLinks) replaced the `Recurse` bool. Error reports `IOException("Is a directory")`.
  RecurseNoLinks skips *name-surrogate* reparse-point dirs (real links); RecurseWithLinks follows them with
  identity-based cycle prevention (a visited-set of `DirectoryId` = volume serial + 128-bit file id via
  `CreateFile(FILE_FLAG_BACKUP_SEMANTICS)` + `GetFileInformationByHandleEx(FileIdInfo)`; option A also
  de-dups diamonds). All `ReportError` paths (enumeration + per-file) now count toward
  `Summary.Errors`. The walk enumerates with `NativeDir` (a `FindFirstFileEx` wrapper,
  blittable `fixed`-buffer `WIN32_FIND_DATA`): each `NativeDirEntry` carries name + attributes + reparse
  **tag** from the scan (no second probe), the tag drives `IsNameSurrogate` classification, and the
  non-recursing path passes `skipDirectories: true` to skip directory entries before their name is even
  materialized.
- Files: `MatchFile`/`ApplyReplaceFile` share one `ProcessFile` (open → `GetFileType` special-file
  rejection → detect → `OnFile` → verb body). Non-`FILE_TYPE_DISK` inputs (device/pipe/socket) are
  reported as `IOException("Not a regular file")`; zero-length regular files are searched/replaced via a
  plain null / zero-length input (empty files can't be mmap'd; the native `posValid` fix accepts it).
- Tests: 303 managed + 470 native (lib) passing.
