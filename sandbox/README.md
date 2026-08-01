# sandbox/ — native scratch / benchmarks

*Not shipped.* A throwaway C++ playground for ad-hoc experiments and quick
performance checks against the native library ([`lib/`](../lib/)) — not part of
the product and not covered by the public API guarantees.

`MobyDick.txt` is bundled purely as a large, real-world block of text to run
transcoding/regex benchmarks against.

If you're looking for the actual tests, see [`test/`](../test/) (native) and
[`managed_test/`](../managed_test/) (.NET).

## Part of

The [UnicodeRegEx](../README.md) project — developer scratch space.
