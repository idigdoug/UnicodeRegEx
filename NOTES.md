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
  (distinct from the deliberate StopFile/StopAll responses). Impl note: with future multithreading the
  throw happens on a worker thread under the sink lock and must propagate out to fault the job.
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
- `-d skip` / `-d recurse`: config (recurse exists; add the exclude-dir list).
- `-d read`: **keep on the list** — reporting "foo is a directory" on Win32 (rather than silently
  skipping) has minor value. Needs a way to surface a directory-as-input to the tool.
- `-R` symlink deref: **config option** (follow directory links or not) — engine-level.
- **Cycle prevention: very-nice-to-have, not a must** (may be non-trivial). Key simplification:
  **cycles are only possible when following directory links** — on NTFS/ReFS a tree can only loop through
  a directory reparse point (junction/symlink); hardlinked directories aren't allowed. So detection is
  only needed when follow-links is ON; with it OFF, no detection required.
  - Detection must be **identity-based, not path-based** (a junction's path differs but points to the
    same place): track the ancestor chain's file identities (`GetFileInformationByHandle` → volume serial
    + file index, or `FILE_ID_INFO` for the 128-bit id on ReFS); refuse to descend into a directory whose
    identity is already an ancestor. One dir-handle open per descent, only when following links.
- **Special files (`-D`)**: disposition (read/skip) is a **PRE-OPEN** decision made during enumeration
  (before mmap), NOT an `OnFile` response. `File.GetAttributes` reveals `ReparsePoint`/`Device`; true
  FIFO/pipe/socket may need `GetFileType` on a handle. Config option on the request.

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
callback responses, and syntax + match-flag settings (both `SyntaxFlags` and `MatchFlags` masks). Remaining:

1. **Ordered filename-filter list** on the request (Include/Exclude per entry) + parser append
   capability, evaluated by the exact grep rule above; separate **exclude-only** directory-filter list
   for `--exclude-dir` (prunes the walk).
2. **Follow-links option** + **cycle prevention** (nice-to-have): identity-based ancestor tracking,
   needed only when following links.
3. **Directory / special-file disposition** — `-d read` ("foo is a directory" report); pre-open
   special-file (`-D`) read/skip config checked during enumeration.

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
  awkward under future multithreaded enumeration (synchronous per-directory decisions across workers).
  Prefer config (recurse + exclude-dir + follow-links, plus a "report directory" option for `-d read`)
  unless a concrete need survives.
- **Manual/computed replacement (deferred):** letting the tool supply its own replacement bytes for a
  match (rather than the engine's `Format()` template). Lean: a **lambda** on the request/apply path,
  NOT a return value from `OnHit`. Revisit only if a real need appears.

## Status snapshot

- Engine is `SearchJob` (async / cancel / progress / dispose; two-pass enumerate→process; sink
  serialized; one-shot). Single-threaded now; multithreaded-processing groundwork (file list + sink
  lock) in place.
- Encoding/binary detection: `EncodingDetector`, ordered per-step-toggleable heuristics (BOM, UTF-16
  NUL-parity, strict UTF-8, binary NUL + control-ratio); `EncodingDetectionOptions` + a `SkipBinaryFiles`
  bool on `SearchRequest`; per-file results via `SearchFile` + `ISearchSink.OnFile`.
- Callbacks: `OnFile`/`OnHit` return `SearchResponse`; `OnFileChanged`; `OnError(path, Exception)`.
  `SearchHit` is a `readonly ref struct { SearchFile File; RegExMatch Match; }` (+ lazy `Text`/`Replacement`).
- Syntax + match flags: `SearchRequest.SyntaxFlags` / `.MatchFlags` (single raw masks), validated by C++
  allow-masks; `ComposeSyntaxFlags`/`SetSyntaxFlags` helpers. Not on the CLI yet (see to-do).
- Filters: `--include` only (single semicolon glob on file names; named files bypass). `--exclude` /
  `--exclude-dir` and the ordered-filter list not yet added (to-do #1).
- Tests: 268 managed + 61 native (lib) passing.
