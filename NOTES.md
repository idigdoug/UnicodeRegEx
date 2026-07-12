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
- **`OnMatch`** (search verb) returns Continue / StopFile / StopAll.
  - StopFile stops the current file's enumeration only (→ grep `-m N`).
  - StopAll stops the whole job (redundant with `job.Cancel()` but far more convenient — keep both).
  - **The apply (rewrite) verb uses `OnApply` instead** (see the callback split below): each match is
    reported *before* its replacement is written, and the returned `ApplyAction` chooses what to write --
    including `StopFile`/`StopAll`, which **abandon the current file** (we bail out of the segment loop
    without committing, so the delete-on-close temp is discarded and the original is left byte-for-byte
    intact; StopFile → next file, StopAll → cancel job). Consequence: a hit can be reported and then
    abandoned, so "was applied" keys off `OnFileChanged`, never the hit callback.
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
  mid-file). Acceptable.

### Per-hit / per-file content (the "SearchHit context" work)
- **`RegExPinnedBytes` is a ref struct**, so it can't live on `SearchFile` (a class). Therefore
  **`OnFile` receives file content as a separate `RegExPinnedBytes` parameter**, not via `SearchFile`.
- **`SearchHit` becomes a `ref struct`** and carries an **entire `RegExMatch`** (itself a ref struct):
  gives the tool submatches, offsets, `Format()`, `CopyInput`, etc. `Text`/`Replacement` become derived
  (lazy) rather than eagerly materialized → a count-only tool (`-c`) pays nothing for strings.
  Unlocks grep `-A/-B/-C` (context), `-n` (line numbers), `-o` (matched slice), and the precise
  `-a/-I` "NUL in unmatched segments" check — all tool-side.
- **Lifetime contract (critical):** a `RegExMatch` is valid **only during the single `OnMatch`/`OnApply`
  call** — the enumerator mutates a shared underlying COM object on each `MoveNext`, so a stored match goes stale
  on the next iteration (tighter than "valid for the file"). `OnFile`'s content bytes are valid only
  while that file is mapped. Rule: **copy anything you need to keep, during the callback.**

### Filtering
- Engine gets an **ordered list of filename filters**, each `{ Include | Exclude, glob }`.
- **Directory filters are a SEPARATE list** (exclude-dir) — relative ordering between filename filters
  and directory filters is meaningless (different names), so they do not interleave.
- **Ordering is preserved through the whole stack.** The request model holds the ordered list; the
  settings layer now carries it too via `GlobListSetting` (a list-valued `Setting` whose `Apply`
  *appends*), so several `--include`/`--exclude` occurrences accumulate in encounter order rather than
  last-wins. The command-line parser appends one entry per occurrence, tagging each with its kind from the
  matching binding (a `--include` binding tagged Include, `--exclude` tagged Exclude — both feed one
  `GlobListSetting`). `SearchSettings.MakeRequest()` copies the ordered list across verbatim.

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
- **Reparse classification is by tag.** The old check used
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
- **Cycle prevention.** Only relevant when following links (NTFS/ReFS loop only through a directory
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
- **Special files (devices / FIFOs / pipes / sockets): consistently an error.** They are structurally
  unsupported: the engine mmaps a real file, so there is nothing to stream from — and the library's point
  is in-place *replace* of files, which a stream can't be. `ProcessFile` does a pre-open `GetFileType`
  check and reports anything that is not `FILE_TYPE_DISK` as an `IOException("Not a regular file")` via
  `OnError` (counts toward `Summary.Errors`) — so a special file is never silently mistaken for a
  successfully-searched empty file.
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
  `SearchRequest.SkipBinaryFiles` (default **true**, consistent with `Recurse` as "common
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

The engine work is complete (see **Decisions (settled)** and the status snapshot), and `SearchSettings` now
drives the full `SearchRequest` surface (syntax/match/format flags, directory disposition, binary handling,
detection steps, parallelism — all wired to settings and the CLI). No engine or settings-coverage work is
outstanding. Remaining work is the **WinForms GUI** (`managed_gui`/`UnicodeRegExGui`): a find /
find-and-selectively-replace tool. It is being built in slices on top of a UI-agnostic core:
- **Slice 1a (done):** `UnicodeRegEx.Tools.Collecting` — the reusable, testable core (below).
- **Slice 1b (done):** a minimal WinForms shell (`MainForm`) — pattern + path inputs, Search/Cancel,
  a `ListView` of hits (file / offset / match), and a monospace context pane showing `Pre[Match]Post` for
  the selected hit. Runs a `SearchJob` + `CollectingSink` on a background thread, validates up front
  (`SearchRequest.Validate`, friendly message not a faulted run), and marshals the throttled `HitsAdded`
  and completion to the UI thread via `BeginInvoke`, appending only the new tail (with a final flush).
  `AnyCPU`+`PreferNativeArm64`, output to `out\Debug` beside the native DLLs.
- **Slice 2a (done):** the pane-swap structure. The settings live in a top host panel (`settingsPanel`,
  `Dock=Top`) that `MainForm` swaps between two distinct `UserControl`s: `CoreSettingsPane` (the expanded
  editor) and `CollapsedSettingsPane` (a one-line summary strip), so the collapsed state is a real control,
  not a resized splitter. Both panes are bound (`Bind`) to the **one** shared `SearchSettings` `MainForm`
  owns; the panes raise intent events (`SearchRequested`/`CancelRequested`/`CollapseRequested` and
  `ExpandRequested`) and `MainForm` owns the swap + the run. The run now flows through
  `SearchSettings.MakeRequest()` / `.Validate()` (friendly `SettingProblem.Message`) instead of a hand-built
  `SearchRequest`. The collapse button lives inside `CoreSettingsPane` (bottom corner). Layout is code-built
  (no per-pane designer files); `MainForm.Designer.cs` is a clean docked layout (host panel + results
  `SplitContainer` `Dock=Fill` + a `StatusStrip`). This slice hosts only the fields the old shell had
  (pattern + path) to prove the mechanics.
- **Slice 2b-core (done):** built out `CoreSettingsPane`'s primary fields and introduced the **Find/Replace
  verb model**. The pane gains a replacement-template box, a **Replace** button beside Search (Search and
  Replace are verbs, not settings — so there is no `Apply` toggle), a **Match case** checkbox (inverts the
  `IgnoreCase` setting), and a **Search subfolders** checkbox (a two-value view over the multi-valued
  `Directories` setting: checked = `RecurseNoLinks`, unchecked = `ReadImmediateFiles`; any recursing
  disposition reads as checked). Both Find and Replace run the engine's **`Match`** verb — neither edits
  files — the only difference is the run mode: Find ignores the template and captures no replacement; Replace
  honors the template and each hit records its replacement (via the new `CollectingSink(captureReplacements)`
  flag) so the results can later be applied (slice 3). `MainForm` threads the mode into `StartRunAsync(bool
  replace)`, constructs the sink accordingly, remembers `lastRunWasReplace`, and shows the captured
  replacement in the context pane (`Pre[Match → Replacement]Post`) in Replace mode. The collapsed summary now
  appends a compact hint of active non-default options (`replace → …`, `match case`, `no subfolders`).
- **Slice 2b-rest / 2c (next):** the remaining `CoreSettingsPane` fields (include/exclude + exclude-dir
  globs, syntax flavor, browse) and the auto-generated advanced property page (from `GroupedSettings` /
  `EditorKind` / `TrySetValue` / `Reset` — surface confirmed present) for the rest of `SearchSettings`, edited
  on a staging copy and committed on OK/Apply. Deferred: how a core-pane checkbox projects onto a multi-value
  setting once the advanced page can set a value outside the checkbox's two (tri-state vs combo); the
  C-escape transform (to live in `SearchRequest`/`SearchJob`, not `SearchSettings`); MRU.
- **Slice 3:** selective replace — a checkbox per hit, then a fresh apply run whose `OnApply` returns
  `Default` for checked / `Original` for unchecked, re-verifying the hit's `Pre`/`Match`/`Post` bytes still
  match to guard against a file changing between preview and apply.

## Status snapshot

- Engine is `SearchJob` (async / cancel / progress / dispose; two-pass enumerate→process; one-shot).
  Phase 2 (file processing) parallelizes across files via
  `SearchRequest.MaxDegreeOfParallelism` (1 = serial default; 0 = auto = `ProcessorCount`; >1 = capped).
  The compiled `RegEx` is shared across workers (immutable + free-threaded via the native FTM; each
  `Match`/`Replace` builds a fresh `MatchEnumerator`), and each file's work is independent (own handle /
  mmap / enumerator / replacement temp file). Sink callbacks are NOT serialized by the job (the old
  `sinkGate` was removed): a single file's callbacks run on one worker thread in order (one file = one
  iteration), but different files run concurrently, so the sink owns the thread-safety of any cross-file
  state; per-file state is race-free and rides on `SearchFile.Context`. The engine's own shared state
  (`CancellationTokenSource`, counters) is interlocked. A throwing sink faults the job with
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
- Callbacks: `OnFile(SearchFile, RegExPinnedBytes)` (bytes = the file's raw content, ref-struct param valid
  only during the call) returns `SearchResponse`; the per-match callback is split by verb -- `OnMatch`
  (search) returns `SearchResponse`, `OnApply` (rewrite) returns `ApplyAction`; `OnFileComplete` (closes the `OnFile` bracket --
  fires once per file `OnFile` accepted, after its last hit, not for skipped/errored/empty files -- so a
  sink can group a file's output under parallelism); `OnFileChanged`; `OnError(path, Exception)`.
  `SearchFile` lets a sink steer/annotate during `OnFile`: `OverrideCodePage(int)` re-decodes the file
  (validated; throws once the file is `Lock()`ed right after `OnFile` returns) and `Context` (`object?`,
  engine never reads it, unlocked) threads per-file state to `OnMatch`/`OnApply`/`OnFileComplete`;
  `LooksBinary` stays a read-only detector verdict.
  THREADING: callbacks are NOT serialized -- a file's callbacks run on one thread in order, but different
  files run concurrently under `MaxDegreeOfParallelism > 1`, so a sink synchronizes only its cross-file
  state (per-file state via `Context` is race-free). At the default degree of 1 it's all single-threaded.
  `SearchHit` is a `readonly ref struct { SearchFile File; RegExMatch Match; }` (+ lazy `Text`/`Replacement`).
  `SearchSinkBase` is an opt-in adapter (defaults steering callbacks to `Continue`, notifications to no-op)
  for consumers that override only what they need; deliberately NOT used by first-party production sinks
  so an `ISearchSink` change stays a compile error there (the base would silently no-op new callbacks --
  documented tradeoff).
- Syntax + match + format flags: `SearchRequest.SyntaxFlags` / `.MatchFlags` / `.FormatFlags` (single raw
  masks), each validated by a C++ allow-mask (`MatchFlagsAreValid` / `FormatFlagsAreValid` reject bits
  outside the exposed set at `Replace`/`ReplaceTo`/`SetFormatTemplate`). Those two allow-mask checks are
  also exposed up the stack -- on COM `IRegExLibrary` and as `RegEx.MatchFlagsAreValid` /
  `RegEx.FormatFlagsAreValid` (same names at every layer) -- so a front-end can validate a mask before a run
  (used by `SearchRequest.Validate`). `FormatFlags` (Perl default; Sed,
  BoostExtensions, NoCopy, FirstOnly) controls replacement interpretation and is threaded into both the
  Match-preview and Apply enumerate options. Boost's whole-template `format_literal` is deliberately NOT
  exposed (a caller escapes the template via the escape helpers instead) and is rejected by the allow-mask.
  `ComposeSyntaxFlags`/`SetSyntaxFlags` helpers. On the CLI as advanced per-flag settings (native names, see
  the settings-model bullet).
- Request validation: `SearchRequest.Validate()` returns the list of `SearchRequestProblem`s (empty when
  valid) and is the engine-level pre-flight gate (a front-end usually reaches it via
  `SearchSettings.Validate()`, which calls `MakeRequest()` then this). It
  checks pattern/paths presence, resolved code page, the match/format-flag masks (via the `RegEx`
  validators above), non-negative `MaxDegreeOfParallelism`, and the enum-typed fields
  (`Verb`/`Directories` via `Enum.IsDefined`; `EncodingDetection` steps against `~All` since it's a
  `[Flags]` set). It also **compiles the pattern** (with `SyntaxFlags`) purely to check validity --
  reporting `PatternInvalid` (and capturing the engine's error text for `DescribeProblemForCommandLine`) --
  and disposes the compiled regex immediately; nothing is cached (no lifetime/staleness concerns). The
  `SearchJob` still compiles its own regex when it runs, so a request that skipped `Validate()` still faults
  cleanly on a bad pattern; pre-validating just moves that failure up front.
- Filters: request holds ordered `FileNameFilters` and `DirectoryFilters` lists (`List<GlobFilter>`);
  `GlobFilterSet` applies last-match-wins, collapsing same-kind runs into one regex each. Filenames use
  grep's default (include unless first filter is an include); directories force default-include (Option B).
  Filters apply to roots and discovered dirs alike (roots not special). Named files bypass. On the CLI,
  `--include`/`--exclude` (file names) and `--exclude-dir` (directories) each **append** one ordered entry
  (repeatable, interleaved in encounter order), backed by `GlobListSetting`s in `SearchSettings`.
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
- Verb / replacement model (cleaned up): the engine has exactly **two actions**, `SearchVerb.Match`
  (report matches, with each match's replacement available as a preview via `SearchHit.Replacement`) and
  `SearchVerb.Apply` (write replacements). The action is decided **solely by `SearchRequest.Verb`** --
  never by `ReplaceTemplate` null-ness (which is gone: `ReplaceTemplate` is now a non-null `string`,
  defaulting to `""`, since it is passed as a BSTR where empty ≡ null) and never by a separate flag (the old
  `Apply` bool was deleted). `SearchHit.Replacement` is always non-null (empty template formats to `""`), so
  there is no preview special-case. `Verb` and `ReplaceTemplate` are independent (an apply with an empty
  template deletes matches), so there is no invalid combination to validate. `MakeRequest` maps
  `--apply` → `Verb`, `--replace` → `ReplaceTemplate`. The CLI shows `=>` based on whether the `Replace`
  template setting is non-empty (`Replace` is a non-null `string`, default `""`).
- Settings model (`SearchSettings` : `SettingGroup`): the shared, front-end-neutral options model and the
  **complete GUI model** (CLI/config are secondary consumers). Settings are public fields discovered by
  reflection; each has a `SettingRole`. `Preference` = persisted the normal (preference-store) way ⇒ shows
  on the GUI's auto-generated advanced property page; `WorkingState` = not persisted that way (transient
  launch intent *or* MRU-remembered inputs like the filter lists) ⇒ stays off the advanced page (the
  primary UI, which is hand-drawn, owns those). Placement thus falls out of the role; the primary UI needs
  no marker because it is manual. `Pattern` and `Paths` live on the model too but as **plain data** (not
  `Setting`s), so they are naturally off the property page and out of generated help — they are primary-UI
  inputs. The model **produces** the engine input: `SearchSettings.MakeRequest()` builds a fully-populated
  `SearchRequest` (the single settings→request translation; `SearchRequest` no longer has `ApplySettings`
  or `ApplyPositionals`, so the dependency points model→request, and "positionals" stays a CLI concept). The
  CLI maps its positionals onto `settings.Pattern`/`Paths` and keeps the default-path `.` policy, then calls
  `MakeRequest()`. Validation is encapsulated in `SearchSettings.Validate()`, which calls `MakeRequest()`,
  runs `request.Validate()`, and maps each `SearchRequestProblem` to the control a front-end should flag:
  `SettingProblem { Problem, Target, Setting?, Message }` where `Target` is `Setting`/`Pattern`/`Paths`/`None`
  (e.g. `UnsupportedCodePage`→Encoding setting, `PatternRequired`/`PatternInvalid`→Pattern,
  `PathRequired`→Paths). Filter lists use `GlobListSetting` (appending `Apply`, kind from the binding `Tag`,
  `ToDisplayString` for MRU/GUI/help, not a CLI round-trip). `Setting.Apply` takes the matched
  `CommandLineBinding` so a multi-binding setting knows which alias fired; `HelpFormatter` renders one line
  per binding, word-wrapped at column 79, and shows a value-option's value with POSIX/GNU syntax --
  `--name=<value>` for a long option, `-x <value>` for a short-only one (the parser accepts both `=` and a
  space at runtime). Naming convention: a setting's root `LongName` is its **persistence key and GUI property-page
  label**, so it must be meaningful and unambiguous *independent of the CLI* — it is not itself a CLI token
  when the setting overrides its bindings (a `ChoiceSetting`/`GlobListSetting`). Hence
  `file-name-filters`/`directory-filters` (CLI: `--include`/`--exclude`/`--exclude-dir`) and `syntax-flavor`
  (CLI: `-E`/`-G`/`-P`/`-F`). A missing-value error names the alias the user typed (`binding.LongName`), not
  the root.
- Property-page surface on `Setting` (Bucket A, lean/single-dialog): alongside the string-based CLI/config
  path (`Apply`/`DefaultText`), each setting has a typed, observable surface a property-page dialog binds to:
  `object? GetValue()` / `bool TrySetValue(object?, out error)` (accepts the setting's own type *or* a
  string, no-throw on bad input), `object? DefaultValue` + `IsDefault` + `Reset()`, an
  `event EventHandler ValueChanged` (raised on change via either `Apply` or `TrySetValue`; each setting is
  one value so no property name), and an `EditorKind` hint (`Toggle`/`Choice`/`Text`/`Integer`/`List`) so
  the dialog picks a control without inspecting the runtime type (e.g. `Encoding` declares `Integer`). The
  dialog decides visibility by filtering on `Role` (`Preference` shown); there is no visibility member.
  `GlobListSetting` opts out: `EditorKind.List`, `GetValue`/`TrySetValue` throw `NotSupportedException` (it
  is `WorkingState`, edited via `Filters`/the primary UI, never on the property page). Adding a future
  scalar setting just declares its `EditorKind` and inherits the rest, so it is GUI-ready on arrival.
- Full `SearchRequest` coverage (Bucket B): `SearchSettings` now exposes a setting for every `SearchRequest`
  capability, so `MakeRequest()` populates the whole request. Multi-bit masks are modeled as one
  `FlagSetting` per bit, composed in `MakeRequest` (`ComposeMatchFlags`/`ComposeFormatFlags`, and the syntax
  modifiers folded through `SetSyntaxFlags`). Advanced flags use **native engine flag names** so their
  docs are discoverable — `--mod-s`/`--mod-x`/`--no-mod-m`/`--collate` (syntax), `--not-bol`…`--continuous`
  /`--match-any` (match_*), `--sed`/`--boost-extensions`/`--no-copy`/`--first-only` (format_*), and detection
  disable-flags `--no-bom`/`--no-utf16-detect`/`--no-utf8-detect`/`--no-nul-binary`/`--no-control-ratio-binary`
  — each prefixed `Advanced:` in its description and sorted last within its category (informal, no separate
  "advanced" axis). `Directories` is a `ChoiceSetting<DirectoryDisposition>` (replacing the old `Recurse`
  flag; keeps `-r`→RecurseNoLinks, adds `-R`→RecurseWithLinks and `--directories-*` for the rest).
  `--binary-files` is a grep-vocab `ChoiceSetting` (`binary`/`without-match`/`text`) mapped to
  `SkipBinaryFiles`. `--parallelism` (a new `Performance` category) maps to `MaxDegreeOfParallelism`. Names
  were audited to avoid clashing with grep options of a different meaning (e.g. `--binary-files` not
  `--binary`). `--locale` (a `ValueSetting<int>` in `Matching`, friendly names `neutral`=0 / `invariant`=0x7F
  or a raw LCID number) maps to a new `SearchRequest.Lcid` — the case-folding/collation locale passed to
  `RegEx.Create` at both compile sites (the job and `Validate`'s `TryCompilePattern`).
- Setting grouping (`SettingCategory`): every `Setting` declares a `SettingCategory` (enum: `Matching`,
  `Replacement`, `Files`, `Encoding`; extensible) alongside its `Role`. `SettingGroup` exposes
  `GroupedSettings` (an ordered `SettingCategoryView { Category, Title, Settings }` list) built by bucketing
  the flat `Settings` by category, emitting categories in enum order and omitting empties; settings within a
  section keep declaration order. Declaration order itself is now pinned deterministically by sorting
  `Collect()` on `FieldInfo.MetadataToken` (reflection field order is unspecified by the CLR). `--help`
  renders one `Title:` section per category (blank line + title, then the options); the GUI property page
  will use the same grouping filtered by `Role`. Titles come from `SettingCategories.DisplayName`.
- Per-verb callbacks + computed replacement: `ISearchSink.OnHit` was split into **`OnMatch`**
  (search verb; returns `SearchResponse` to steer -- Continue/StopFile/StopAll -- nothing is written) and
  **`OnApply`** (apply verb; returns an **`ApplyAction`** that decides what to write for each match). An
  `ApplyAction` is a `readonly struct` over `ApplyActionKind { Default, Original, Delete, Custom, StopFile,
  StopAll }`: static members `Default` (write the formatted template), `Original` (write the matched input
  unchanged), `Delete` (write nothing), `StopFile`/`StopAll` (abandon this file's rewrite -- current match
  not written, temp discarded, original untouched), and factory `Custom(ArraySegment<byte>)` for a
  **computed replacement** (bytes written verbatim; the caller owns the code page; a default/empty segment
  == `Delete`). No implicit conversions -- the caller names the action explicitly. The engine still owns
  the crash-safe / atomic / encoding-preserving write; the callback only chooses *what*. `SearchSinkBase`
  defaults `OnMatch`→`Continue`, `OnApply`→`ApplyAction.Default`. The interop write is a no-copy
  `RegExSequentialStream.Write(ArraySegment<byte>)` → `ISequentialStream.RemoteWrite(ref firstByte, count,
  out _)` kept inside the wrapper assembly (Tools never touches `Interop.ISequentialStream`, avoiding
  embedded-interop-type/PIA problems). A `SearchHit.Verb` was deliberately NOT added (the verb is implied
  by which callback fired).
- GUI-agnostic collecting core (`UnicodeRegEx.Tools.Collecting`, for the WinForms find/replace UI):
  `HitRecord` is a **storable** snapshot of a match copied out of the ref-struct `SearchHit` during the
  callback — `SearchFile`, `MatchFileOffset`, and byte blobs `PreMatchBytes`/`MatchBytes`/`PostMatchBytes`/
  `ReplacementBytes` (strings decoded on demand via `RegExEncoding.FromCodePage(File.CodePage)`). Context
  windows are a bounded byte count (64) clamped at the file start/end, so they double as a later
  apply-time staleness guard. `CollectingSink : SearchSinkBase` captures each `OnMatch` into a `HitRecord`
  and accumulates them thread-safely (`Hits` append-only snapshot + collected `Errors`), raising a
  **throttled** `HitsAdded` event (every ~100 ms or 256 hits) and an **unthrottled** `ErrorsAdded` event
  (errors are low-volume) so a UI can stream both hits and errors as they occur — fired on a worker thread,
  so a subscriber marshals.
  Replace mode it formats each replacement through a per-file `RegExMemoryStream` held on `SearchFile.Context`
  (`Reset()` per hit, disposed at `OnFileComplete`); in Find mode it makes no stream and leaves each
  `ReplacementBytes` empty (the engine verb is `Match` either way — the flag only controls whether the
  replacement is materialized for a later apply). Fully unit-tested with no UI.
- WinForms GUI (`managed_gui`/`UnicodeRegExGui`, `net48`, `AnyCPU`+`PreferNativeArm64`, output to
  `out\Debug` beside the native DLLs): `MainForm` is a find/replace tool over the `Collecting` core — a
  details `ListView` (file / offset / match), a monospace context pane, and a `StatusStrip`. It runs
  `SearchJob` + `CollectingSink`, validates up front, and marshals the sink's throttled `HitsAdded` and its
  (unthrottled) `ErrorsAdded` events plus run completion to the UI thread (`BeginInvoke`), appending only the
  new tail. Each row's `Tag` carries its model (a `HitRecord` for hits); errors stream in as they occur as
  distinguished rows (path in File, message in Match, `Firebrick` fore color, `Tag` = the `SearchError`), and
  the context pane shows the selected row's detail (match context for a hit, `{path}: {message}` for an
  error) by branching on `Tag`.
  The settings surface is a **pane swap** (slice 2a): a top host panel (`settingsPanel`, `Dock=Top`) holds
  either `CoreSettingsPane` (expanded editor) or `CollapsedSettingsPane` (a one-line summary that expands
  back), so collapsing hands the screen to the results while keeping a glance-back. `MainForm` owns the
  **one** shared `SearchSettings`, `Bind`s both panes to it, and orchestrates the swap; panes only raise
  intent events.
  Verbs (slice 2b-core): `CoreSettingsPane`'s rows follow the old app — **Search for** (pattern), **Replace
  with** (template), **In files** (include globs), **In folders** (path) — each paired with the button that
  acts on it (Search / Replace / Browse). The four input boxes are editable `ComboBox`es (so each
  can become an MRU dropdown later; persistence isn't wired yet). The **In files** box is a
  semicolon-separated include-file glob list (e.g. `*.cs;*.h`, pushed to `FileNameFilters` as all-`Include`
  filters — the ordered/mixed include-exclude case is intentionally out of scope for the core page since these
  are `WorkingState`, never on the advanced page), a **Search** and a **Replace** button (Search/Replace are
  verbs, not settings — no `Apply` toggle), a **Match case** checkbox (inverts `IgnoreCase`), a **Search
  subfolders** checkbox (a *tri-state* view over `Directories`: checked = a recursing disposition, unchecked =
  `ReadImmediateFiles`, indeterminate = `Error`/`Skip` — a push in that state leaves it untouched, and a
  checked push preserves an already-recursing value like `RecurseWithLinks` rather than downgrading it), and a
  **Perl regular expression** checkbox (a *tri-state* view over `SyntaxFlavor`: checked = `Perl`, unchecked =
  `Literal`/fixed strings, indeterminate = a flavor the checkbox doesn't model such as `Basic`/`Extended` — a
  push in that state leaves `SyntaxFlavor` untouched). Both tri-state checkboxes use `AutoCheck = false` so a
  user click only toggles checked/unchecked (never into indeterminate, which is code-set only). A
  **Browse...** button (aligned with the Path row) opens a `FolderBrowserDialog` seeded with the current path
  and writes the chosen folder back to the Path box. Both run buttons execute the engine's `Match` verb (no
  file edits here); `MainForm.StartRunAsync(bool replace)` constructs `CollectingSink(captureReplacements:
  replace)`, so Find ignores the template and Replace records each hit's replacement (shown as `Pre[Match →
  Replacement]Post` in the context pane) for a later selective apply. The collapsed summary appends a compact
  hint of active non-default options. `CoreSettingsPane`, `CollapsedSettingsPane`, and `ActionBar` are all
  VS-designer-compatible splits following one shared contract — controls/layout in a `*.Designer.cs`
  (`InitializeComponent`, absolute `Location`/`Size` + `Anchor`, named `*_Click` handlers) with a nominal
  `*.resx`; the code-behind `*.cs` raises intent events and exposes imperative setters, while `MainForm` owns
  all logic (the panes' settings binding via `Bind`/`PullFromSettings`/`PushToSettings`/`UpdateSummary`,
  `SetRunning`, `BrowseForPath`, tri-state helpers).
  Run/results verbs that are not settings live in a single always-visible **`ActionBar`** placed below the
  settings pane (a full-width `Fixed3D` `actionBarSeparator` line divides them; above the results): **Apply**,
  **Select All**, **Select None**, a **progress bar**, and **Cancel**. The verb buttons enable/disable but
  never hide (a click target can't disappear mid-interaction). The bar is full-width (`Dock=Top`, no
  `MaximumSize` — an earlier width cap fought the dock stretch and clipped Cancel); the left cluster
  (Apply/Select All/Select None) is fixed, and `ActionBar.OnResize` lays out the progress bar to grow with the
  bar up to a cap (`ProgressMaxWidth`) with Cancel placed just to its right (so empty space falls to the far
  right). The full-width separator lives on the form (not in the bar) so it spans the whole window. Both
  the separator and the `ActionBar` are **designer-declared on `MainForm`** (created in `InitializeComponent`,
  dock stack settingsPanel -> separator -> actionBar -> results via `Controls.Add` order, `ActionBar` events
  serialized designer-style); the swapped panes (`corePane`/`collapsedPane`) stay runtime-created since the
  designer can't express two controls taking turns in one `settingsPanel` slot. It has no `Bind(settings)`
  (run/results state only); it raises
  `ApplyRequested`/`SelectAllRequested`/`SelectNoneRequested`/`CancelRequested` and exposes
  `SetRunning`/`SetResultsState`/`SetProgress`. `MainForm` centralizes run-UI state in `UpdateRunUiState` and
  drives progress from `SearchJob.ProgressChanged` (marshaled like the sink events): marquee while
  `Enumerating`, determinate `CompletedFileCount/TotalFileCount` while `Processing`, empty when idle. Cancel is
  an operation verb, so it moved out of `CoreSettingsPane` into the bar; the `StatusStrip` is kept for text
  only (run summary + validation). A C-style escape-translation checkbox was
  deemed unnecessary for now (Perl/Extended flavors already handle C-style escapes; a toggle would only add
  value for Literal/Basic, deferred until wanted). The auto-generated advanced options page is still to come
  (slice 2c).
- Selective replace (slice 3, done): after a **Replace** run the results rows are checkable
  (`hitList.CheckBoxes` on only for Replace; error rows vetoed via `ItemCheck`; all rows default checked).
  **Select All/None** toggle them and **Apply** runs a standalone **`ReplaceJob`** (`UnicodeRegEx.Tools.Engine`,
  parallel in shape to `SearchJob`) over just the chosen `HitRecord`s — it does **not** re-enumerate or re-run
  the regex. It groups the chosen hits by file, memory-maps each file, and before rewriting **re-verifies**
  each match's captured `Pre`/`Match`/`Post` bytes still surround its `MatchFileOffset` (a staleness guard):
  valid matches are rewritten with the preview's captured `HitRecord.ReplacementBytes` (WYSIWYG, no re-format)
  via `RegEx.CreateReplacementFileStream` (`Write` unchanged spans + replacement bytes, then `Flush` +
  `MoveTo(ReplaceExisting)`); a changed match is left as-is and counted skipped-stale. Only files with at
  least one valid edit are rewritten (no no-op rewrites). One bad file is isolated (per-file try/catch →
  `Errors`), the rest still apply. This slice is serial and cancels at file boundaries; `ReplaceJob` exposes
  `RunAsync`/`Cancel`/`ProgressChanged`/`State`/`TotalFileCount`/`CompletedFileCount`/`AppliedCount`/
  `SkippedStaleCount`/`ChangedFiles`/`Errors`, shaped to grow toward mid-file cancel (the write stream
  supports `LinkCancellation`) and cross-file parallelism later. `MainForm.StartApplyAsync` drives it with the
  same progress/Cancel/`UpdateRunUiState` scaffolding as a search (via a distinct `replaceJob` field); on
  completion the results are cleared and a summary shown ("Applied N replacement(s)[, M skipped (file
  changed)][, K error(s)]"). Headless unit-tested (all / subset / none / stale-skip / multi-file / per-file
  error isolation). (The earlier `ApplyingSink`/`Verb=Apply` approach was retired — `SearchJob` only partially
  overlapped: it re-enumerated and re-matched, and rewrote matched files byte-identically even when nothing
  changed.)
- Persistence (MRU + preferences): `PersistedState` + `StateStore` (`UnicodeRegEx.Tools`, front-end-neutral,
  unit-tested) round-trip GUI state to `%APPDATA%\UnicodeRegEx\state.xml` via hand-written `XmlReader`/`XmlWriter`
  (the schema is small/stable, so no `XmlSerializer` reflection or temp assembly; `PersistedState` is a plain
  data model with no `[Xml*]` attributes, and `StateStore` owns the I/O with shared element/attribute-name
  constants; missing/corrupt file -> empty, never blocks startup). Two kinds of state, partitioned by
  `SettingRole`: **WorkingState** inputs get **MRU lists** (most-recent-first, de-duplicated, capped at 10) keyed
  by `pattern` / `paths` / `replace` / `file-name-filters`; **Preference** settings are saved/restored as scalars
  keyed by `LongName` via the new `Setting.GetPersistedValue()` (pairs with the `Apply(string, DefaultBinding)`
  load path; round-trip guarded by a data-driven test over every preference). `MainForm` loads on launch (seeds
  the `file-name-filters` MRU from a hard-coded list so there's always a top; applies persisted preferences to
  `settings`; restores the Search/Replace/In-files box values from their MRU top while the In-folders (Path) box
  is populated from `Environment.CurrentDirectory`; fills the combo dropdowns), records the committed inputs to
  MRU on each run, and saves preferences + MRU on `FormClosing`. `CoreSettingsPane` exposes
  `SetMruItems(key, items)` and the shared `*Key` constants; `GlobListSetting` throws from `GetPersistedValue`
  (list-valued, persisted via MRU).
- **Rich-text details pane (done):** `contextBox` is a `RichTextBox`; `ShowSelectedContext` renders
  `HitRecord`'s segments as styled runs via an `AppendRun` helper — Find mode highlights the match (bold +
  pale-yellow background); Replace mode strikes the matched text (pale-red) immediately followed by the
  replacement (pale-green), no arrow. A cached `FontStyle.Strikeout` font is disposed with the form. No
  engine/Tools change.
- **Line/column positions (done):** `RegExLineCounter` (managed wrapper) is a forward, non-decreasing cursor
  giving a 1-based line number **and** code-unit column without decoding the whole file (line breaks LF/CR/CRLF;
  UTF-8/UTF-16; `nuint` line + column; a fresh counter reads line 0 / column 1, and any `AdvanceTo` puts it on
  line 1). Collected **eagerly** at search time: `SearchRequest.TrackLineNumbers` (opt-in; `Clone`d) gates it,
  `SearchSettings.MakeRequest` always sets it true (the GUI always gets positions), and `SearchJob` builds one
  counter per file from the enumerable's new `Input`/`InputCodePage` accessors (`RegExMatchEnumerable` /
  `RegExSegmentEnumerable`) and advances it to each match's begin offset (matches arrive in ascending order).
  Line/column flow through `SearchHit` (0 when not tracked) into `HitRecord`; the GUI's former "Offset" column
  is now **"Position"** showing `Line,Column`.
- **Fit-and-finish backlog** (feature-parity with the old app reached; these are polish, not critical):
  multithreaded `ReplaceJob` (mid-file cancel via `RegExFileStream.LinkCancellation` + cross-file parallelism);
  the auto-generated advanced-options page; and **double-click a result to open the match in the user's editor**
  positioned on its line (the line/column data now exists — this is the editor-launch UX only).
- Tests: 412 managed (+ 1 Perf, category-excluded) + 481 native (lib) passing.
