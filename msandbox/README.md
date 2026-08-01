# msandbox/ — managed scratch

*Not shipped.* A throwaway C# console playground for ad-hoc experiments against
the managed interop layer ([`managed/`](../managed/)) — not part of the product
and not covered by the public API guarantees.

Handy for quickly poking at the `UnicodeRegEx` .NET API by hand; the code here is
disposable and changes freely.

If you're looking for the actual tests, see [`managed_test/`](../managed_test/)
(.NET) and [`test/`](../test/) (native).

## Part of

The [UnicodeRegEx](../README.md) project — developer scratch space.
