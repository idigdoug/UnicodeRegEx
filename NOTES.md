# UnicodeRegEx — Working Notes

Scratchpad for in-progress design decisions and next steps. Not user documentation.

## Next steps (search tool / engine)

Planned settings to add (each is shared `SearchSettings` + `SearchRequest` plumbing, mirroring the
existing options):

- **Syntax flags** — beyond the `Syntax` choice (Extended/Basic/Perl/Literal) already wired via
  `ChoiceSetting`/`CommandLineBinding` (`-E`/`-G`/`-P`/`-F`). Consider exposing the remaining
  `RegExSyntaxFlags` bits that make sense as options.
- **Match flags** — expose relevant `RegExMatchFlags` as settings.
- **exclude-dir** — exclude directories from the recursive walk by name (the directory-name filter we
  deferred when adding `--include`). Slots into `SearchJob.EnumerateDirectory` (the seam noted in the
  glob slice).

## Open design question: `--include` / `--exclude` ordering (grep parity)

grep supports both `--include` and `--exclude` (and `--exclude-dir`) in **arbitrary order**, evaluated
**in the order given** on the command line. That order-sensitivity does **not** map cleanly to the
current settings model, where each setting holds one value and "last wins" — a single
`Include` string + a single `Exclude` string loses the interleaving.

Need to decide:
- Whether order-sensitive include/exclude is worth supporting at all (vs. a simpler "include filter AND
  NOT exclude filter" with fixed precedence, which maps fine to the current settings system).
- If we do want grep-style ordered evaluation, it likely needs a different representation than a plain
  `ValueSetting<string>` — e.g. an ordered list of (kind, glob) filter entries captured during parsing,
  which is a `CommandLineParser`/`SettingGroup` extension rather than a single setting value.

Current state: only `--include` exists (single semicolon-separated glob list, applied to `Path.GetFileName`
of directory-walked files; explicitly named files bypass it). `--exclude`/`--exclude-dir` not yet added.

## Status snapshot (for context)

- Engine is `SearchJob` (async/cancel/progress/dispose, two-pass enumerate→process, sink serialized,
  one-shot). Single-threaded for now; multi-threaded processing groundwork (file-list + sink lock) in place.
- Encoding/binary detection: `EncodingDetector` with ordered, per-step-toggleable heuristics
  (BOM, UTF-16 NUL-parity, strict UTF-8, binary NUL + control-ratio), config via
  `EncodingDetectionOptions` carried on `SearchRequest`; binary disposition (Skip/Error/Search) on the
  request; per-file results via `SearchFile` (+ `ISearchSink.OnFile`).
- Detection toggles and binary disposition are **not yet exposed on the CLI** (engine/request only) —
  intended as "advanced" options / GUI checkboxes later.
- `SearchHit` is still intentionally minimal (matched text + optional replacement); richer shape
  (byte offsets / context) deferred until the GUI's needs are known.
