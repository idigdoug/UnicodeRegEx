#pragma once
#include <UnicodeRegEx.h>

#include <TextEncoding.h>

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

// Returns true if lowBits is valid for the given encoding
// (multiple of the encoding's element size).
bool
InputIsAligned(TextEncoding encoding, LONGLONG lowBits) noexcept;

// Returns true and sets encoding if codePage is valid and lowBits is valid for it
// (multiple of the encoding's element size).
_Success_(return)
bool
TextEncodingForCodePageIfAligned(unsigned codePage, LONGLONG lowBits, _Out_ TextEncoding* encoding) noexcept;
