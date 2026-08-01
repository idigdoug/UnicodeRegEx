# managed_cli/ — UniGrep (command-line tool)

*Layer 5 (CLI).* A command-line search-and-replace tool built on the shared tools library
([`managed_tools/`](../managed_tools/)). Produces **`UniGrep.exe`** (command: `unigrep`). Targets
.NET Framework 4.8.

The CLI is intentionally a **thin presentation** over the Tools library — it parses arguments into a
`SearchRequest`, runs a `SearchJob`, and prints results. It also serves as lightweight scaffolding for
developing and exercising the Tools library; the GUI ([`managed_gui/`](../managed_gui/)) is the primary
end-user tool.

## Usage

```pwsh
unigrep "TODO|FIXME" src\
unigrep --ignore-case "error" logs\ --recurse
unigrep "\bcolour\b" docs\ --replace "color"          # preview
unigrep "\bcolour\b" docs\ --replace "color" --apply  # write in place
```

Options align with GNU grep conventions where reasonable (`-i`/`--ignore-case`, `-r`/`--recurse`,
`-E`/`-F`/`-G`/`-P` for syntax flavor, `--include` globs, …). Run `unigrep --help` for the full list.
GNU grep is used as a *design oracle* for which capabilities are worth having, not as a compatibility
target.

## What's here

- **`Program.cs`** — the entry point: settings composition (defaults < app config < command line),
  running the job, and rendering results/errors/exit codes.
- **`Tools/Settings/AppConfigSource.cs`** — applies `<appSettings>` defaults onto the settings before
  the command line. This is the one settings piece kept out of the shared library because it depends on
  `System.Configuration`.
- **`App.config`** — optional default settings (each key matches an option's long name).

## Part of

The [UnicodeRegEx](../README.md) project — layer 5.
