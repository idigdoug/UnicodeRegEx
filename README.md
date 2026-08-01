# UnicodeRegEx

**Unicode-correct regular expressions over text in any encoding — matching on Unicode code points (UTF-32), with identical results regardless of the input's byte encoding.**

UnicodeRegEx is a Windows-focused, layered toolkit for regex search and replace where the matching
semantics are defined in terms of **Unicode scalar values**, not code units. Input in UTF-8, UTF-16
(LE/BE), Latin-1, or any single-byte Windows code page is transcoded to UTF-32 for matching, so the
same pattern behaves identically no matter how the text was stored on disk.

It is built for a few different audiences, who may care about different layers:

- **C++ developers** who want a `regex_traits` for `char32_t` that uses Win32 for locale/charset
  support **without depending on ICU** — a lighter-weight alternative to Boost's `icu_regex_traits`.
- **C++ developers** who want fast, header-based iterators that expose SBCS / Latin-1 / UTF-8 / UTF-16
  text as a sequence of `char32_t` code points (similar to Boost's `u8_to_u32_iterator` /
  `u16_to_u32_iterator`, but better-optimized and covering more encodings).
- **.NET developers** who want Boost's regex engine, with encoding-independent Unicode matching,
  exposed through a clean managed API.
- **End users** who just want a search-and-replace tool (GUI or CLI) that handles Unicode and multiple
  file encodings correctly.

> **Status:** Pre-1.0 and under active development. Public interfaces (the COM ABI, the C++ headers,
> and the managed API) may change before a 1.0 release.

## Why this exists

Most regex engines match on the encoding's *code units* (UTF-16 code units, or bytes), so results can
differ depending on how identical text was stored, and non-BMP characters or multibyte sequences need
special handling in the pattern. UnicodeRegEx instead transcodes all input to UTF-32 up front and
matches on code points, giving one consistent behavior across encodings. It does this using the Boost
regex engine and Win32 locale APIs.

## The layers

The project is a stack; each layer is usable on its own.

| # | Component | What it is | For whom |
|---|-----------|------------|----------|
| 1a | [`inc/TextEncoding.h`](inc/TextEncoding.h) | Almost header-only (one `.cpp` needed for SBCS) transcoding: `char32_t` iterators over SBCS/Latin-1/UTF-8/UTF-16 input, and in-place conversion from UTF-32 to those encodings. | C++ devs needing fast code-point iteration/conversion |
| 1b | [`inc/WindowsChar32RegexTraits.h`](inc/WindowsChar32RegexTraits.h) | A `regex_traits` for `char32_t` that works with `std::basic_regex` and `boost::basic_regex`, using Win32 for charset/locale — an ICU-free analogue of Boost's `icu_regex_traits`. | C++ devs wanting `char32_t` regex without ICU |
| 2 | `UnicodeRegEx_<arch>.dll` | Native DLL exposing the Boost regex engine through COM interfaces; accepts multiple text encodings; matches on UTF-32. | Native/COM consumers |
| 3 | `UnicodeRegEx.dll` | .NET, a thin interop layer over the native DLL. | .NET developers |
| 4 | `UnicodeRegEx.Tools.dll` | .NET, the shared search-and-replace engine and settings model used by the tools. | .NET devs building search/replace UX |
| 5 | `UniRex.exe` / `UniGrep.exe` | .NET GUI and CLI search-and-replace tools. | End users |

## Repository layout

| Directory | Contents |
|-----------|----------|
| [`inc/`](inc/) | Public C++ headers (`TextEncoding.h`, `WindowsChar32RegexTraits.h`) and the COM IDL |
| [`lib/`](lib/) | Native static library (`UnicodeRegExLib`) |
| [`dll/`](dll/) | Native COM DLL (`UnicodeRegEx_<arch>.dll`) |
| [`managed/`](managed/) | .NET interop wrapper (`UnicodeRegEx.dll`) |
| [`managed_tools/`](managed_tools/) | .NET shared tools library (`UnicodeRegEx.Tools.dll`) |
| [`managed_cli/`](managed_cli/) | CLI tool (`UniGrep.exe`) |
| [`managed_gui/`](managed_gui/) | WinForms GUI tool (`UniRex.exe`) |
| [`managed_test/`](managed_test/) | .NET unit tests |
| [`test/`](test/) | Native unit tests |
| [`sandbox/`](sandbox/) | Native scratch / benchmarks |
| [`external/`](external/) | Git submodules: `boost.regex`, `microsoft.wil` |

## Building

**Prerequisites**

- Windows.
- Visual Studio 2026 (Community/Professional/Enterprise) or the matching Build Tools, with:
  - **Desktop development with C++** (includes the Windows SDK), and
  - **.NET desktop development**, plus the **.NET Framework 4.8** developer pack.
- Git.

**Clone with submodules** (required — the build needs `external/boost.regex` and `external/microsoft.wil`):

```pwsh
git clone --recursive https://github.com/idigdoug/UnicodeRegEx.git
# or, if already cloned:
git submodule update --init --recursive
```

**Build**

Open `UnicodeRegEx.sln` in Visual Studio, or from a Developer PowerShell:

```pwsh
msbuild UnicodeRegEx.sln /p:Configuration=Release /p:Platform=x64
```

Supported platforms: **x64**, **x86**, **ARM64**. The native DLL is per-architecture
(`UnicodeRegEx_x64.dll`, etc.); the managed assemblies are AnyCPU and load the matching native DLL at
runtime. Build output lands under `out/`.

## Quick start

### C++: iterate any encoding as code points (layer 1a)

```cpp
#include <TextEncoding.h>
#include <span>
#include <string_view>

std::u8string_view text = u8"café";
auto [begin, end] = Utf8().MakeCodePointRange(
	std::span(text.data(), text.size()));

for (auto it = begin; it != end; ++it)
{
	char32_t codePoint = *it; // decoded UTF-32 scalar value
}
```

The encoding types are `Latin1`, `Sbcs`, `Utf8`, `Utf16LE`, and `Utf16BE`; each exposes
`MakeCodePointRange` (decode to `char32_t`) and `ConvertInPlace` (encode UTF-32 to that encoding).
Code-point predicates live in the `CodePoint` namespace.

### C++: char32_t regex without ICU (layer 1b)

```cpp
#include <WindowsChar32RegexTraits.h>
#include <boost/regex.hpp>

boost::basic_regex<char32_t, WindowsChar32RegexTraits> pattern(/* char32_t pattern */);
```

### .NET: search and replace (layers 3–4)

```csharp
using UnicodeRegEx;

using var regex = RegEx.Create("caf\u00e9");

// Search
foreach (var match in regex.EnumerateMatches("a café and another café"))
{
	System.Console.WriteLine(match.Text);
}

// Replace
string result = regex.Replace("banana", "X"); // "bXnXnX"
```

### Command line (layer 5)

```pwsh
unigrep "TODO|FIXME" src\
unigrep --ignore-case "error" logs\ --recurse
unigrep "\bcolour\b" docs\ --replace "color"        # preview
unigrep "\bcolour\b" docs\ --replace "color" --apply # write in place
```

`UniGrep`'s command-line options align with GNU grep conventions where reasonable
(e.g. `-i`/`--ignore-case`, `-r`/`--recurse`). Run `unigrep --help` for the full list.

## Documentation

Each major directory has its own README describing that layer in more detail — see `inc/`, `lib/`,
`dll/`, `managed/`, `managed_tools/`, `managed_cli/`, and `managed_gui/`.

## License

UnicodeRegEx is licensed under the [MIT License](LICENSE).

It builds on Boost.Regex (Boost Software License 1.0) and the Windows Implementation Libraries (MIT).
See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for full attributions and license texts.
