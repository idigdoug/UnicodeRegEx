#include "pch.h"
#include "InputTranscoder.h"

bool
InputTranscoder::RangeIsInBounds(LONGLONG offset, LONGLONG size, size_t bufferSize) noexcept
{
    auto const offsetU = static_cast<size_t>(offset);
    auto const sizeU = static_cast<size_t>(size);

    if (offsetU > bufferSize || sizeU > bufferSize - offsetU)
    {
        return false;
    }

    return true;
}

bool
InputTranscoder::OffsetAndSizeAreAlignedForEncoding(LONGLONG offset, LONGLONG size, RegExEncoding encoding) noexcept
{
    switch (encoding)
    {
    case RegExEncoding_utf16le:
    case RegExEncoding_utf16be:
        return ((offset | size) & 1) == 0;

    case RegExEncoding_latin1:
    case RegExEncoding_utf8:
        return true;

    default:
        return false;
    }
}
