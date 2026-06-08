#include "pch.h"
#include "MatchEnumerator.h"
#include "RegEx.h"

#include <utf.h>

template<class IteratorT>
MatchEnumerator<IteratorT>::MatchEnumerator(
    RegEx const& regex,
    boost::regex_constants::match_flag_type matchFlags,
    _In_reads_bytes_(size) void const* data,
    size_t size,
    size_t startByteOffset)
    : m_regex(regex)
    , m_matchFlags(matchFlags)
    , m_begin()
    , m_pos()
    , m_end()
    , m_matchResults()
{
    using CharT = typename IteratorT::input_type;
    if (0 != ((reinterpret_cast<size_t>(data) | size) & (sizeof(CharT) - 1)))
    {
        THROW_HR(E_INVALIDARG);
    }

    auto rangeAndPos = IteratorT::FromSpanAndByteOffset(
        std::span(static_cast<CharT const*>(data), size / sizeof(CharT)),
        startByteOffset);
    if (rangeAndPos.pos == IteratorT())
    {
        // startByteOffset was out of range or at an invalid position.
        THROW_HR(E_INVALIDARG);
    }

    m_begin = rangeAndPos.begin;
    m_pos = rangeAndPos.pos;
    m_end = rangeAndPos.end;
}

template<class IteratorT>
bool
MatchEnumerator<IteratorT>::InitialMatch(bool wholeStringMatch)
{
    if (wholeStringMatch)
    {
        // boost::regex_match has no "base" parameter, so it cannot give the engine
        // access to characters in [m_begin, m_pos) for lookbehind assertions or
        // word-boundary checks. Simulate it with regex_search instead:
        //   - match_continuous anchors the match at m_pos (no scanning forward)
        //   - the base parameter (m_begin) enables full lookbehind
        //   - we verify the match consumed the entire remainder to m_end
        // This is semantically equivalent to regex_match but works correctly when
        // startByteOffset > 0.
        bool const found = boost::regex_search(
            m_pos,
            m_end,
            m_matchResults,
            m_regex.GetRegex(),
            m_matchFlags | boost::regex_constants::match_continuous,
            m_begin);
        return found && m_matchResults[0].second == m_end;
    }
    else
    {
        // Search starts at m_pos but passes m_begin as the base parameter so that
        // lookbehind assertions can see characters in [m_begin, m_pos).
        return boost::regex_search(
            m_pos,
            m_end,
            m_matchResults,
            m_regex.GetRegex(),
            m_matchFlags,
            m_begin);
    }
}

template<class IteratorT>
bool
MatchEnumerator<IteratorT>::AdvanceMatch()
{
    // Behaves like regex_iterator::operator++ (C++ standard [re.regiter.incr]).

    auto start = m_matchResults[0].second;
    bool const wasZeroLength = (m_matchResults[0].first == m_matchResults[0].second);

    if (wasZeroLength && start == m_end)
    {
        // End-of-sequence iterator.
        return false;
    }

    bool found = false;
    if (wasZeroLength)
    {
        // Try to find a non-null match at the same position.
        found = boost::regex_search(
            start,
            m_end,
            m_matchResults,
            m_regex.GetRegex(),
            m_matchFlags | boost::regex_constants::match_not_null | boost::regex_constants::match_continuous,
            m_begin);

        if (!found)
        {
            // No non-null match here. Advance one position and fall through to the normal case.
            ++start;
        }
    }

    if (!found)
    {
        // Normal case: search from start. The base parameter m_begin enables lookbehind
        // and acts as the match_prev_avail anchor without the caller needing to set
        // match_prev_avail explicitly.
        found = boost::regex_search(
            start,
            m_end,
            m_matchResults,
            m_regex.GetRegex(),
            m_matchFlags,
            m_begin);
    }

    return found;
}

// Explicit instantiations for the four supported input encodings.
// Adding a new encoding requires adding the corresponding instantiation here.
template class MatchEnumerator<latin1::CodePointIterator>;
template class MatchEnumerator<utf8::CodePointIterator>;
template class MatchEnumerator<utf16le::CodePointIterator>;
template class MatchEnumerator<utf16be::CodePointIterator>;
