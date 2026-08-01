# managed/ — .NET interop wrapper (UnicodeRegEx.dll)

*Layer 3.* A thin, idiomatic .NET wrapper over the native COM DLL. Targets **netstandard2.0**, so it
can be consumed from .NET Framework and modern .NET alike. Loads the native
`UnicodeRegEx_<arch>.dll` matching the host process architecture at runtime.

## Public surface

- **`RegEx`** — compile a pattern (`RegEx.Create`) and run `EnumerateMatches`, `EnumerateSegments`,
  `Replace`, transcode/escape helpers.
- **Input** — `RegExInput` (implicitly wraps `string`, `char[]`, byte buffers, and `SafeBuffer`
  sources with a code page), `RegExPinnedBytes`.
- **Results** — `RegExMatch`, `RegExSubMatch`, `RegExMatchResult`, and the enumerators
  (`RegExMatchEnumerable`/`Enumerator`, `RegExSegmentEnumerable`/`Enumerator`, `RegExSegment`).
- **Streams** — `RegExMemoryStream`, `RegExFileStream` (and the `RegExSequentialStream` base).
- **Code pages & encodings** — `RegExCodePage` (well-known code-page constants), `RegExEncoding`
  (`Encoding` resolution helpers), `RegExLineCounter`.
- **Enums / options** — `RegExSyntaxFlags`, `RegExMatchFlags`, `RegExFormatFlags`, and the option
  structs in `RegExMisc` (`RegExMatchOptions`, `RegExReplaceOptions`, `RegExEnumerateOptions`).
- **`RegExException`** — surfaces engine errors (e.g. an invalid pattern).

`RegEx.cs` also contains the `NativeMethods` P/Invoke declarations (one per architecture) for
`UnicodeRegExLibraryCreate`.

## Dependencies

The native DLL (via the generated `UnicodeRegEx.Interop` types) — no third-party NuGet packages.

## Part of

The [UnicodeRegEx](../README.md) project — layer 3. See the [root README](../README.md#quick-start)
for a usage example.
