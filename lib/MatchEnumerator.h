#pragma once

class RegEx;

// Drives match iteration over a typed input range using the standard C++
// regex_iterator semantics (with corrected zero-length-match handling).
//
// Shared by RegExMatchBase (which exposes per-match accessors via
// IRegExMatchEnumerator) and RegEx::ReplaceImpl (which feeds matches to a
// replacement loop). Using one implementation keeps both paths in sync.
//
// Lifetime: the referenced RegEx must outlive this enumerator. The input
// buffer (described by data/size) must remain valid for the lifetime of this
// object as well.
template<class EncodingT>
class MatchEnumerator
{
    using IteratorT = typename EncodingT::CodePointIterator;

    RegEx const& m_regex;
    boost::regex_constants::match_flag_type const m_matchFlags;
    IteratorT m_begin; // start of input (for regex_search base parameter / lookbehind context)
    IteratorT m_pos;   // start of search (allows pre-context in [m_begin, m_pos))
    IteratorT m_end;
    boost::match_results<IteratorT> m_matchResults;

public:

    MatchEnumerator(
        RegEx const& regex,
        boost::regex_constants::match_flag_type matchFlags,
        _In_reads_bytes_(size) void const* data,
        size_t size,
        size_t startByteOffset,
        EncodingT encoding);

    // First match. wholeStringMatch selects regex_match vs regex_search.
    // Returns true if a match was found. May throw.
    bool
    InitialMatch(bool wholeStringMatch);

    // Subsequent match. Returns true if a match was found. Implements C++
    // regex_iterator::operator++ semantics: zero-length matches are retried
    // at the same position with match_not_null | match_continuous before
    // advancing past the position. May throw.
    bool
    AdvanceMatch();

    // Inspect the most recent successful match. Undefined if no successful
    // match has been retrieved (caller's responsibility to track state).
    boost::match_results<IteratorT> const&
    MatchResults() const noexcept { return m_matchResults; }

    // Start of the input range; used by RegEx::ReplaceImpl to anchor the
    // unmatched-text copy when startByteOffset > 0 (bytes before the offset
    // are emitted as part of the prefix before the first match).
    IteratorT
    Begin() const noexcept { return m_begin; }

    // End of the input range; used by RegEx::ReplaceImpl to copy the tail
    // after the last successful match.
    IteratorT
    End() const noexcept { return m_end; }
};
