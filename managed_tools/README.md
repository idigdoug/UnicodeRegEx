# managed_tools/ — .NET shared tools library (UnicodeRegEx.Tools.dll)

*Layer 4.* The front-end-neutral search-and-replace engine and settings model shared by both tools
([`managed_cli/`](../managed_cli/) UniGrep and [`managed_gui/`](../managed_gui/) UniRex). Targets
**netstandard2.0** and depends only on the interop wrapper ([`managed/`](../managed/)) — no
`System.Configuration` or other host-specific dependencies (the one config-bound piece,
`AppConfigSource`, deliberately lives in the CLI instead).

## Public surface

- **The operation model** — `SearchRequest` (a mutable, front-end-neutral description of what to
  search/replace, its editable inputs, `Verb`, and `Validate()`), which both tools populate and the
  engine consumes.
- **The engine** — `Engine/SearchJob` (a one-shot, cancelable, progress-reporting search or
  search-and-replace over a set of paths) and `Engine/SearchResults` (`ISearchSink`, `SearchHit`,
  `SearchSummary`, `SearchJobState`). Results stream through the sink; the job serializes callbacks.
- **The settings mechanism** — `Settings/Setting` (`Setting`, `FlagSetting`, `ValueSetting<T>`,
  `ChoiceSetting<T>`, `SettingRole`, `CommandLineBinding`) and `Settings/SettingGroup`. Settings are
  declared as fields, discovered by reflection, and drive command-line parsing, help text, config
  overlay, and (in the GUI) property binding.
- **`SearchSettings`** — the concrete option set for the search/replace tools.
- **Command line** — `CommandLine` (a small parser over a `SettingGroup`) and `HelpFormatter`.
- **Helpers** — `CodePages` (code-page alias parse/format + resolution) and `GlobToRegex` (compile a
  `*.cs;*.txt`-style glob list to a `Regex`).

## Design note

The split between "capability" (belongs on `SearchRequest`/the engine, shared by both tools) and
"presentation" (belongs in each front-end) is deliberate. This is what lets the CLI and GUI share this
library without either's idioms leaking into it.

## Part of

The [UnicodeRegEx](../README.md) project — layer 4.
