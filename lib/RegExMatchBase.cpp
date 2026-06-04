#include "pch.h"
#include "RegExMatchBase.h"
#include "RegEx.h"

#include <utf.h>

static constexpr RegExString
MakeString(_In_reads_bytes_(size) void const* data, UINT_PTR size, RegExEncoding encoding) noexcept
{
    return {
        .data_ptr = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(data)),
        .size = static_cast<LONGLONG>(size),
        .encoding = encoding,
    };
}

template<class IteratorT>
RegExMatchBase::SearchState<IteratorT>::SearchState(_In_reads_bytes_(size) void const* data, size_t size, size_t startByteOffset)
    : begin()
    , pos()
    , end()
    , matchResults()
{
    using CharT = typename IteratorT::input_type;
    if (0 != ((reinterpret_cast<size_t>(data) | size) & (sizeof(CharT) - 1)))
    {
        THROW_HR(E_INVALIDARG);
    }

    auto rangeAndPos = IteratorT::FromSpanAndByteOffset(std::span(static_cast<CharT const*>(data), size / sizeof(CharT)), startByteOffset);
    if (rangeAndPos.pos == IteratorT())
    {
        THROW_HR(E_INVALIDARG);
    }

    begin = rangeAndPos.begin;
    pos = rangeAndPos.pos;
    end = rangeAndPos.end;
}

RegExMatchBase::~RegExMatchBase() = default;

RegExMatchBase::RegExMatchBase(
    _In_ RegEx* regex,
    _In_ RegExString const* pInput,
    _In_ UINT_PTR startByteOffset,
    RegExMatchFlags flags)
    : m_refCount(1)
    , m_matchFlags(static_cast<boost::regex_constants::match_flag_type>(flags))
    , m_regex(regex)
    , m_inputData(reinterpret_cast<void const*>(static_cast<UINT_PTR>(pInput->data_ptr)))
    , m_inputSize(static_cast<UINT_PTR>(pInput->size))
    , m_inputEncoding(pInput->encoding)
    , m_state(RegExEnumerationState_not_started)
    , m_variantSearchState()
    , m_formatTemplate()
    , m_formatFlags(boost::regex_constants::format_default)
    , m_outputBuffer()
{
    // Validate the encoding parameter and initialize the search state with iterators.
    switch (m_inputEncoding)
    {
    case RegExEncoding_latin1:
        m_variantSearchState.emplace<SearchStateLatin1>(m_inputData, m_inputSize, startByteOffset);
        break;

    case RegExEncoding_utf8:
        m_variantSearchState.emplace<SearchStateUtf8>(m_inputData, m_inputSize, startByteOffset);
        break;

    case RegExEncoding_utf16le:
        m_variantSearchState.emplace<SearchStateUtf16LE>(m_inputData, m_inputSize, startByteOffset);
        break;

    case RegExEncoding_utf16be:
        m_variantSearchState.emplace<SearchStateUtf16BE>(m_inputData, m_inputSize, startByteOffset);
        break;

    default:
        THROW_HR(E_INVALIDARG);
    }
}

ULONG __stdcall
RegExMatchBase::AddRef() noexcept
{
    return InterlockedIncrementNoFence(&m_refCount);
}

ULONG __stdcall
RegExMatchBase::Release() noexcept
{
    ULONG ref = InterlockedDecrementRelease(&m_refCount);
    if (ref == 0)
    {
        delete this;
    }

    return ref;
}

bool
RegExMatchBase::DoInitialSearch(bool wholeStringMatch)
{
    assert(m_state == RegExEnumerationState_not_started);

    bool found = std::visit([this, wholeStringMatch](auto& state)
        { return VisitInitialSearch(state, wholeStringMatch); },
        m_variantSearchState);

    if (found)
    {
        m_state = RegExEnumerationState_enumerating;
    }

    return found;
}

HRESULT
RegExMatchBase::get_Input(_Out_ RegExString* pInput) noexcept
{
    *pInput = MakeString(m_inputData, m_inputSize, m_inputEncoding);
    return S_OK;
}

HRESULT
RegExMatchBase::get_SubMatchCount(_Out_ UINT32* pCount) noexcept
{
    if (m_state != RegExEnumerationState_enumerating)
    {
        *pCount = 0;
    }
    else
    {
        *pCount = std::visit([this](auto& state)
            { return VisitGetSubMatchCount(state); },
            m_variantSearchState);
    }

    return S_OK;
}

HRESULT
RegExMatchBase::GetSubMatch(UINT32 subMatchIndex, _Out_ RegExSubMatch* pSubMatch) noexcept
{
    HRESULT hr;
    *pSubMatch = {};

    if (m_state != RegExEnumerationState_enumerating)
    {
        // get_SubMatchCount is 0, so any index is out of range.
        hr = E_INVALIDARG;
    }
    else
    {
        hr = std::visit([this, subMatchIndex, pSubMatch](auto& state)
            { return VisitGetSubMatch(state, subMatchIndex, pSubMatch); },
            m_variantSearchState);
    }

    return hr;
}

HRESULT
RegExMatchBase::GetSubMatchString(
    UINT32 subMatchIndex,
    RegExEncoding subMatchEncoding,
    _Out_ RegExString* pSubMatchString) noexcept
{
    HRESULT hr;
    *pSubMatchString = {};

    if (m_state != RegExEnumerationState_enumerating)
    {
        // get_SubMatchCount is 0, so any index is out of range.
        hr = E_INVALIDARG;
    }
    else
    {
        try
        {
            m_outputBuffer.clear();
            hr = std::visit([this, subMatchIndex](auto& state)
                { return VisitGetSubMatchString(state, subMatchIndex); },
                m_variantSearchState);

            if (SUCCEEDED(hr))
            {
                if (hr != S_OK)
                {
                    // No match for the specified subMatchIndex, return with encoding == 0.
                    assert(hr == S_FALSE);
                    hr = S_OK;
                }
                else
                {
                    hr = TranscodeOutput(subMatchEncoding, pSubMatchString);
                }
            }
        }
        catch (...)
        {
            hr = wil::ResultFromCaughtException();
        }
    }

    return hr;
}

HRESULT
RegExMatchBase::SetFormatTemplate(BSTR formatTemplate, RegExFormatFlags formatFlags) noexcept
{
    HRESULT hr;

    try
    {
        static_assert(sizeof(formatTemplate[0]) == sizeof(char16_t), "BSTR must be UTF-16");
        auto formatIterators = utf16le::CodePointIterator::FromSpan(std::span(
            reinterpret_cast<char16_t const*>(formatTemplate),
            SysStringLen(formatTemplate)));
        m_formatTemplate.assign(formatIterators.begin, formatIterators.end);
        m_formatFlags = static_cast<boost::regex_constants::match_flag_type>(formatFlags);
        hr = S_OK;
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    return hr;
}

HRESULT
RegExMatchBase::Format(
    RegExEncoding outputEncoding,
    _Out_ RegExString* pOutput) noexcept
{
    HRESULT hr;
    *pOutput = {};

    if (m_state != RegExEnumerationState_enumerating)
    {
        hr = E_NOT_VALID_STATE;
    }
    else
    {
        try
        {
            m_outputBuffer.clear();
            hr = std::visit([this](auto& state)
                { return VisitFormat(state); },
                m_variantSearchState);
            if (SUCCEEDED(hr))
            {
                hr = TranscodeOutput(outputEncoding, pOutput);
            }
        }
        catch (...)
        {
            hr = wil::ResultFromCaughtException();
        }
    }

    return hr;
}

HRESULT
RegExMatchBase::get_State(_Out_ RegExEnumerationState* pState) noexcept
{
    *pState = m_state;
    return S_OK;
}

HRESULT
RegExMatchBase::NextMatch(_Out_ VARIANT_BOOL* pFound) noexcept
{
    HRESULT hr;
    bool found = false;

    if (m_state == RegExEnumerationState_finished)
    {
        hr = E_NOT_VALID_STATE;
    }
    else
    {
        try
        {
            hr = std::visit([this](auto& state)
                { return VisitNextMatch(state); },
                m_variantSearchState);
            found = m_state == RegExEnumerationState_enumerating;
        }
        catch (...)
        {
            hr = wil::ResultFromCaughtException();
        }
    }

    *pFound = found ? VARIANT_TRUE : VARIANT_FALSE;
    return hr;
}

HRESULT
RegExMatchBase::VisitNextMatch(std::monostate) noexcept
{
    assert(false);
    return E_UNEXPECTED;
}

template<class IteratorT>
HRESULT
RegExMatchBase::VisitNextMatch(
    SearchState<IteratorT>& state) noexcept(false)
{
    assert(m_state != RegExEnumerationState_finished);

    bool found;
    if (m_state == RegExEnumerationState_not_started)
    {
        // First search: start from pos (not begin) to allow lookbehind in begin..pos.
        m_state = RegExEnumerationState_enumerating;
        found = boost::regex_search(
            state.pos,
            state.end,
            state.matchResults,
            m_regex->GetRegex(),
            m_matchFlags,
            state.begin);
    }
    else
    {
        // Subsequent search: behaves like regex_iterator::operator++
        // (C++ standard [re.regiter.incr]).

        auto start = state.matchResults[0].second;
        bool wasZeroLength = (state.matchResults[0].first == state.matchResults[0].second);

        if (wasZeroLength && start == state.end)
        {
            // End-of-sequence iterator.
            m_state = RegExEnumerationState_finished;
            return S_OK;
        }

        found = false;
        if (wasZeroLength)
        {
            // Try to find a non-null match at the same position.
            found = boost::regex_search(
                start,
                state.end,
                state.matchResults,
                m_regex->GetRegex(),
                m_matchFlags | boost::regex_constants::match_not_null | boost::regex_constants::match_continuous,
                state.begin);

            if (!found)
            {
                // Increment start and fall through to the normal case.
                ++start;
            }
        }

        if (!found)
        {
            // Normal case: search from start. Not using match_prev_avail (instead we pass the base parameter).
            found = boost::regex_search(
                start,
                state.end,
                state.matchResults,
                m_regex->GetRegex(),
                m_matchFlags,
                state.begin);
        }
    }

    if (!found)
    {
        m_state = RegExEnumerationState_finished;
    }

    return S_OK;
}

bool
RegExMatchBase::VisitInitialSearch(std::monostate, bool) noexcept
{
    assert(false);
    return false;
}

template<class IteratorT>
bool
RegExMatchBase::VisitInitialSearch(
    SearchState<IteratorT>& state,
    bool wholeStringMatch) noexcept(false)
{
    if (wholeStringMatch)
    {
        return boost::regex_match(
            state.pos,
            state.end,
            state.matchResults,
            m_regex->GetRegex(),
            m_matchFlags);
    }
    else
    {
        return boost::regex_search(
            state.pos,
            state.end,
            state.matchResults,
            m_regex->GetRegex(),
            m_matchFlags,
            state.begin);
    }
}

UINT32
RegExMatchBase::VisitGetSubMatchCount(std::monostate) noexcept
{
    assert(false);
    return 0;
}

template<class IteratorT>
UINT32
RegExMatchBase::VisitGetSubMatchCount(
    SearchState<IteratorT>& state) noexcept
{
    return static_cast<UINT32>(state.matchResults.size());
}

HRESULT
RegExMatchBase::VisitGetSubMatch(std::monostate, UINT32, _Inout_ RegExSubMatch*) noexcept
{
    assert(false);
    return E_UNEXPECTED;
}

template<class IteratorT>
HRESULT
RegExMatchBase::VisitGetSubMatch(
    SearchState<IteratorT>& state,
    UINT32 subMatchIndex,
    _Inout_ RegExSubMatch* pSubMatch) noexcept
{
    auto const& matchResults = state.matchResults;

    if (subMatchIndex >= matchResults.size())
    {
        *pSubMatch = {};
        return E_INVALIDARG;
    }

    auto const& submatch = matchResults[subMatchIndex];
    if (!submatch.matched)
    {
        pSubMatch->input_offset = 0;
        pSubMatch->size = 0;
        pSubMatch->matched = VARIANT_FALSE;
    }
    else
    {
        auto const firstOffset = submatch.first.ByteOffset(m_inputData);
        auto const secondOffset = submatch.second.ByteOffset(m_inputData);
        pSubMatch->input_offset = static_cast<LONGLONG>(firstOffset);
        pSubMatch->size = static_cast<LONGLONG>(secondOffset - firstOffset);
        pSubMatch->matched = VARIANT_TRUE;
    }

    return S_OK;
}

HRESULT
RegExMatchBase::VisitGetSubMatchString(std::monostate, UINT32) noexcept
{
    assert(false);
    return E_UNEXPECTED;
}

template<class IteratorT>
HRESULT
RegExMatchBase::VisitGetSubMatchString(
    SearchState<IteratorT>& state,
    UINT32 subMatchIndex) noexcept
{
    auto const& matchResults = state.matchResults;

    if (subMatchIndex >= matchResults.size())
    {
        return E_INVALIDARG;
    }

    auto const& submatch = matchResults[subMatchIndex];
    if (!submatch.matched)
    {
        return S_FALSE;
    }
    else
    {
        std::copy(submatch.first, submatch.second, std::back_inserter(m_outputBuffer));
        return S_OK;
    }
}

HRESULT
RegExMatchBase::VisitFormat(std::monostate) noexcept
{
    assert(false);
    return E_UNEXPECTED;
}

template<class IteratorT>
HRESULT
RegExMatchBase::VisitFormat(
    SearchState<IteratorT>& state) noexcept(false)
{
    state.matchResults.format(
        std::back_inserter(m_outputBuffer),
        m_formatTemplate,
        m_formatFlags);
    return S_OK;
}

HRESULT
RegExMatchBase::TranscodeOutput(
    RegExEncoding outputEncoding,
    _Out_ RegExString* pOutput) noexcept(false)
{
    void const* data;
    size_t size;

    switch (outputEncoding)
    {
    case RegExEncoding_latin1:
    {
        auto result = latin1::ConvertInPlace(m_outputBuffer);
        data = result.data();
        size = result.size_bytes();
        break;
    }
    case RegExEncoding_utf8:
    {
        auto result = utf8::ConvertInPlace(m_outputBuffer);
        data = result.data();
        size = result.size_bytes();
        break;
    }
    case RegExEncoding_utf16le:
    {
        auto result = utf16le::ConvertInPlace(m_outputBuffer);
        data = result.data();
        size = result.size_bytes();
        break;
    }
    case RegExEncoding_utf16be:
    {
        auto result = utf16be::ConvertInPlace(m_outputBuffer);
        data = result.data();
        size = result.size_bytes();
        break;
    }
    default:
        *pOutput = {};
        return E_INVALIDARG;
    }

    *pOutput = MakeString(data, size, outputEncoding);
    return S_OK;
}
