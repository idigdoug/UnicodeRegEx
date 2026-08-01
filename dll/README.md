# dll/ — Native COM DLL (UnicodeRegEx_&lt;arch&gt;.dll)

*Layer 2.* Packages [`lib/UnicodeRegExLib`](../lib/) into the shipping native DLL that exposes the
regex engine through COM.

## What's here

- **`dll.cpp`** — the DLL entry points and the single exported factory function.
- **`UnicodeRegEx.def`** — exports **`UnicodeRegExLibraryCreate`**, the one entry point: it hands back
  an `IRegExLibrary`, from which callers create `IRegEx` instances and everything else. (`IRegExLibrary`
  is free-threaded; created regex objects follow the threading described in the IDL.)
- **`resource.h`** — version/resource definitions.

The DLL is built per architecture and named `UnicodeRegEx_x64.dll`, `UnicodeRegEx_x86.dll`, or
`UnicodeRegEx_ARM64.dll`. The .NET interop layer ([`managed/`](../managed/)) P/Invokes
`UnicodeRegExLibraryCreate` in the DLL matching the host process architecture.

## Interfaces

Defined in [`inc/UnicodeRegEx.idl`](../inc/UnicodeRegEx.idl) and implemented in [`lib/`](../lib/):
`IRegExLibrary`, `IRegEx`, `IRegExMatchEnumerator`, `IRegExMatchResults`, `IRegExMemoryStream`,
`IRegExFileStream`.

## Part of

The [UnicodeRegEx](../README.md) project — layer 2.
