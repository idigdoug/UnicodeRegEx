#pragma once
#include <UnicodeRegEx.h>

// Returns true if a RegExBytes is a valid whole-input descriptor: size is
// non-negative and a non-empty buffer has a non-null base. Used by the
// public entry points that treat input.size as the buffer length (Match,
// Search, EnumerateMatches, Replace, ReplaceTo, Transcode, TranscodeTo).
// The managed wrapper never produces a negative size, but the COM interface
// is callable directly with a LONGLONG, so a negative size must be rejected
// before it is cast to size_t (which would otherwise become a huge bound and
// drive an out-of-bounds read). data is intentionally NOT range-checked: a
// pointer is a bag of bits (high bits are legitimately set, and a 32-bit
// high pointer sign-extends to a negative LONGLONG).
bool
InputIsValid(RegExBytes input) noexcept;

// Returns true if (offset, size) lie entirely within bufferSize (no overflow,
// and offset + size <= bufferSize).
bool
RangeIsInBounds(LONGLONG offset, LONGLONG size, size_t bufferSize) noexcept;

// Returns true if size and offset are valid for the given encoding
// (multiples of the encoding's element size). offset is checked separately
// since some scenarios pass element-aligned offsets without sizes.
// Returns false for invalid encoding.
bool
OffsetAndSizeAreAlignedForEncoding(LONGLONG offset, LONGLONG size, RegExEncoding encoding) noexcept;
