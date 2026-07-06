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

// Returns true if flags contains only match flags this library accepts as input.
// The allowed set is the exposed RegExMatchFlags input bits; everything else is
// rejected, including boost-internal state (match_init/match_max), unused bits,
// format bits (which must not arrive on the match-flags parameter), unsupported
// options (match_posix / match_all / match_extra / match_nosubs), and
// match_prev_avail -- the enumerator supplies that implicitly via its base
// iterator (see MatchEnumerator::AdvanceMatch), so a caller must not set it.
bool
MatchFlagsAreValid(RegExMatchFlags flags) noexcept;

// Returns true if flags contains only format bits this library exposes and accepts.
// The allowed set is the exposed RegExFormatFlags input bits; everything else is
// rejected, including format_literal (not exposed -- a caller that wants a literal
// replacement escapes the template instead) and any other boost format bits.
bool
FormatFlagsAreValid(RegExFormatFlags flags) noexcept;

// Returns true and sets encoding if codePage is valid and lowBits is valid for it
// (multiple of the encoding's element size).
_Success_(return)
bool
TextEncodingForCodePageIfAligned(unsigned codePage, LONGLONG lowBits, _Out_ TextEncoding* encoding) noexcept;
