#pragma once
#include <RepStrRegEx.h>

class OutputSink;

// Helpers shared between RegExMatchBase (CopyInput/CopyInputTo) and the future
// RegExLibrary Transcode/TranscodeTo methods. All take pre-validated parameters
// in the sense that the caller has resolved start offset and size into a single
// (data, size) tuple. These helpers do the encoding-specific alignment checks
// and code-point iteration.
struct InputTranscoder
{
public:

    // Returns true if (offset, size) lie entirely within bufferSize (no overflow,
    // and offset + size <= bufferSize).
    static bool
    RangeIsInBounds(LONGLONG offset, LONGLONG size, size_t bufferSize) noexcept;

    // Returns true if size and offset are valid for the given encoding
    // (multiples of the encoding's element size). offset is checked separately
    // since some scenarios pass element-aligned offsets without sizes.
    // Returns false for invalid encoding.
    static bool
    OffsetAndSizeAreAlignedForEncoding(LONGLONG offset, LONGLONG size, RegExEncoding encoding) noexcept;
};
