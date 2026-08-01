# lib/ — Native static library (UnicodeRegExLib)

The native implementation that backs the COM DLL. Builds the static library `UnicodeRegExLib`, which
[`dll/`](../dll/) links into `UnicodeRegEx_<arch>.dll`. Also consumed directly by the native tests in
[`test/`](../test/) and the [`sandbox/`](../sandbox/) benchmarks.

## What's here

- **The regex engine wrapper** — `RegEx`, `RegExLibrary`, `RegExCheck`: the Boost.Regex engine driven
  with `char32_t` and [`WindowsChar32RegexTraits`](../inc/WindowsChar32RegexTraits.h), with input
  transcoded to UTF-32 for matching.
- **COM object implementations** — `RegExMatchBase`, `RegExMatchResults`, `RegExMatchEnumerator`,
  `MatchEnumerator` implement the interfaces declared in
  [`inc/UnicodeRegEx.idl`](../inc/UnicodeRegEx.idl).
- **I/O** — `RegExMemoryStream`, `RegExFileStream`, `OutputSink`: the stream types and the output path
  for replace results.
- **Support** — `InputValidation` (argument/bounds checking), `TextEncoding.cpp` (the SBCS support for
  [`TextEncoding.h`](../inc/TextEncoding.h)), `WindowsChar32RegexTraits.cpp`.

## Dependencies

- **Boost.Regex** (`external/boost.regex`, Boost Software License 1.0) — the regex engine, compiled in
  standalone mode (no dependency on the rest of Boost).
- **WIL** (`external/microsoft.wil`, MIT) — header-only Windows helpers.

See [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) for full attributions.

## Part of

The [UnicodeRegEx](../README.md) project — the implementation behind layer 2.
