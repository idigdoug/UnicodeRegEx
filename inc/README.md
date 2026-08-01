# inc/ — Public C++ headers and the COM contract

Public interface for the C++ layers and the native DLL. Everything here is meant to be included or
referenced by consumers.

## Contents

- **`TextEncoding.h`** — *Layer 1a.* Almost-header-only text transcoding (one `.cpp`,
  [`lib/TextEncoding.cpp`](../lib/TextEncoding.cpp), is needed only for SBCS support). Provides:
  - `char32_t` iterators that decode `Latin1` / `Sbcs` / `Utf8` / `Utf16LE` / `Utf16BE` input to
	Unicode scalar values (`MakeCodePointRange`), and
  - in-place conversion from UTF-32 to those encodings (`ConvertInPlace`).
  - Code-point predicates live in the `CodePoint` namespace.

- **`WindowsChar32RegexTraits.h`** — *Layer 1b.* A `regex_traits` implementation for `char32_t` that
  works with `std::basic_regex` and `boost::basic_regex`, using Win32 APIs for charset/locale support.
  An ICU-free analogue of Boost's `icu_regex_traits`.

- **`UnicodeRegEx.idl`** — The COM interface definitions for the native DLL (*Layer 2*):
  `IRegExLibrary` (the factory), `IRegEx`, `IRegExMatchEnumerator`, `IRegExMatchResults`,
  `IRegExMemoryStream`, and `IRegExFileStream`. The MIDL-generated header/typelib are the contract the
  native DLL implements and the .NET interop layer consumes.

## Dependencies

`TextEncoding.h` and `WindowsChar32RegexTraits.h` depend only on the C++ standard library and Win32.
`WindowsChar32RegexTraits.h` is used together with Boost.Regex (or `std::regex`).

## Part of

The [UnicodeRegEx](../README.md) project — layers 1 and 2.
