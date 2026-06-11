#include "pch.h"
#include "RegExMatchBase.h"
#include "RegEx.h"
#include "InputTranscoder.h"

#include <utf.h>

static constexpr RegExBytes
MakeString(_In_reads_bytes_(size) void const* data, UINT_PTR size) noexcept
{
    return {
        .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(data)),
        .size = static_cast<LONGLONG>(size),
    };
}

RegExMatchBase::~RegExMatchBase() = default;

RegExMatchBase::RegExMatchBase(
    _In_ RegEx* regex,
    RegExBytes const& input,
    RegExEncoding inputEncoding,
    UINT_PTR startByteOffset,
    RegExMatchFlags flags)
    : m_refCount(1)
    , m_regex(regex)
    , m_inputData(reinterpret_cast<void const*>(static_cast<UINT_PTR>(input.data)))
    , m_inputSize(static_cast<UINT_PTR>(input.size))
    , m_inputEncoding(inputEncoding)
    , m_state(RegExEnumerationState_not_started)
    , m_variantEnumerator()
    , m_outputSink()
    , m_formatTemplate()
    , m_formatFlags(boost::regex_constants::format_default)
{
    auto const matchFlags = static_cast<boost::regex_constants::match_flag_type>(flags);

    // Validate the encoding parameter and construct the encoding-specific enumerator.
    switch (m_inputEncoding)
    {
    case RegExEncoding_latin1:
        m_variantEnumerator.emplace<EnumeratorLatin1>(*regex, matchFlags, m_inputData, m_inputSize, startByteOffset);
        break;

    case RegExEncoding_utf8:
        m_variantEnumerator.emplace<EnumeratorUtf8>(*regex, matchFlags, m_inputData, m_inputSize, startByteOffset);
        break;

    case RegExEncoding_utf16le:
        m_variantEnumerator.emplace<EnumeratorUtf16LE>(*regex, matchFlags, m_inputData, m_inputSize, startByteOffset);
        break;

    case RegExEncoding_utf16be:
        m_variantEnumerator.emplace<EnumeratorUtf16BE>(*regex, matchFlags, m_inputData, m_inputSize, startByteOffset);
        break;

    default:
        assert(false); // Should have been validated by caller.
        THROW_HR(E_INVALIDARG);
    }
}

bool
RegExMatchBase::DoInitialSearch(bool wholeStringMatch)
{
    assert(m_state == RegExEnumerationState_not_started);

    bool found = std::visit([this, wholeStringMatch](auto& enumerator)
        { return VisitInitialSearch(enumerator, wholeStringMatch); },
        m_variantEnumerator);

    if (found)
    {
        m_state = RegExEnumerationState_enumerating;
    }

    return found;
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

HRESULT
RegExMatchBase::get_Input(_Out_ RegExBytes* pInput) noexcept
{
    *pInput = MakeString(m_inputData, m_inputSize);
    return S_OK;
}

HRESULT
RegExMatchBase::get_InputEncoding(_Out_ RegExEncoding* pEncoding) noexcept
{
    *pEncoding = m_inputEncoding;
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
        *pCount = std::visit([this](auto& enumerator)
            { return VisitGetSubMatchCount(enumerator); },
            m_variantEnumerator);
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
        hr = std::visit([this, subMatchIndex, pSubMatch](auto& enumerator)
            { return VisitGetSubMatch(enumerator, subMatchIndex, pSubMatch); },
            m_variantEnumerator);
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
RegExMatchBase::Format(_Out_ BSTR* pOutputString) noexcept
{
    HRESULT hr;
    *pOutputString = {};

    if (m_state != RegExEnumerationState_enumerating)
    {
        hr = E_NOT_VALID_STATE;
    }
    else
    {
        try
        {
            m_outputSink.ResetToVector(RegExEncoding_utf16le);

            hr = std::visit([this](auto& enumerator)
                { return VisitFormat(enumerator); },
                m_variantEnumerator);

            if (SUCCEEDED(hr))
            {
                auto bytes = m_outputSink.FinishVector();
                *pOutputString = SysAllocStringLen(
                    reinterpret_cast<OLECHAR const*>(bytes.data()),
                    static_cast<UINT>(bytes.size() / sizeof(OLECHAR)));
                hr = *pOutputString ? S_OK : E_OUTOFMEMORY;
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
RegExMatchBase::FormatTo(
    _In_ ISequentialStream* outputStream,
    RegExEncoding outputEncoding) noexcept
{
    HRESULT hr;

    if (outputStream == nullptr)
    {
        hr = E_POINTER;
    }
    else if (!RegExEncodingIsValid(outputEncoding))
    {
        hr = E_INVALIDARG;
    }
    else if (m_state != RegExEnumerationState_enumerating)
    {
        hr = E_NOT_VALID_STATE;
    }
    else
    {
        try
        {
            m_outputSink.ResetToStream(outputEncoding, outputStream);

            hr = std::visit([this](auto& enumerator)
                { return VisitFormat(enumerator); },
                m_variantEnumerator);

            if (SUCCEEDED(hr))
            {
                m_outputSink.FinishStream();
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
RegExMatchBase::CopyInput(
    LONGLONG inputOffset,
    LONGLONG size,
    _Out_ BSTR* pOutputString) noexcept
{
    *pOutputString = nullptr;

    if (!InputTranscoder::RangeIsInBounds(inputOffset, size, m_inputSize) ||
        !InputTranscoder::OffsetAndSizeAreAlignedForEncoding(inputOffset, size, m_inputEncoding))
    {
        return E_INVALIDARG;
    }

    HRESULT hr;
    try
    {
        std::span<BYTE const> input(
            static_cast<BYTE const*>(m_inputData) + static_cast<size_t>(inputOffset),
            static_cast<size_t>(size));

        std::span<BYTE const> output;
        if (m_inputEncoding == RegExEncoding_utf16le)
        {
            output = input;
        }
        else
        {
            m_outputSink.ResetToVector(RegExEncoding_utf16le);
            m_outputSink.AppendBytes(input, m_inputEncoding);
            output = m_outputSink.FinishVector();
        }

        *pOutputString = SysAllocStringLen(
            reinterpret_cast<OLECHAR const*>(output.data()),
            static_cast<UINT>(output.size() / sizeof(OLECHAR)));
        hr = *pOutputString ? S_OK : E_OUTOFMEMORY;
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    return hr;
}

HRESULT
RegExMatchBase::CopyInputTo(
    LONGLONG inputOffset,
    LONGLONG size,
    _In_ ISequentialStream* outputStream,
    RegExEncoding outputEncoding) noexcept
{
    if (outputStream == nullptr)
    {
        return E_POINTER;
    }
    else if (
        !RegExEncodingIsValid(outputEncoding) ||
        !InputTranscoder::RangeIsInBounds(inputOffset, size, m_inputSize) ||
        !InputTranscoder::OffsetAndSizeAreAlignedForEncoding(inputOffset, size, m_inputEncoding))
    {
        return E_INVALIDARG;
    }

    HRESULT hr;
    try
    {
        std::span<BYTE const> input(
            static_cast<BYTE const*>(m_inputData) + static_cast<size_t>(inputOffset),
            static_cast<size_t>(size));

        if (m_inputEncoding == outputEncoding)
        {
            hr = WriteAllBytesToStream(outputStream, input);
        }
        else
        {
            m_outputSink.ResetToStream(outputEncoding, outputStream);
            m_outputSink.AppendBytes(input, m_inputEncoding);
            m_outputSink.FinishStream();
            hr = S_OK;
        }
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
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
            hr = std::visit([this](auto& enumerator)
                { return VisitNextMatch(enumerator); },
                m_variantEnumerator);
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
    MatchEnumerator<IteratorT>& enumerator) noexcept(false)
{
    assert(m_state != RegExEnumerationState_finished);

    bool found;
    if (m_state == RegExEnumerationState_not_started)
    {
        m_state = RegExEnumerationState_enumerating;
        found = enumerator.InitialMatch(/*wholeStringMatch*/ false);
    }
    else
    {
        found = enumerator.AdvanceMatch();
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
    MatchEnumerator<IteratorT>& enumerator,
    bool wholeStringMatch) noexcept(false)
{
    return enumerator.InitialMatch(wholeStringMatch);
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
    MatchEnumerator<IteratorT>& enumerator) noexcept
{
    return static_cast<UINT32>(enumerator.MatchResults().size());
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
    MatchEnumerator<IteratorT>& enumerator,
    UINT32 subMatchIndex,
    _Inout_ RegExSubMatch* pSubMatch) noexcept
{
    auto const& matchResults = enumerator.MatchResults();

    if (subMatchIndex >= matchResults.size())
    {
        return E_INVALIDARG;
    }

    auto const& submatch = matchResults[subMatchIndex];
    if (!submatch.matched)
    {
        pSubMatch->offset = 0;
        pSubMatch->size = 0;
        pSubMatch->matched = VARIANT_FALSE;
    }
    else
    {
        auto const firstOffset = submatch.first.ByteOffset(m_inputData);
        auto const secondOffset = submatch.second.ByteOffset(m_inputData);
        pSubMatch->offset = static_cast<LONGLONG>(firstOffset);
        pSubMatch->size = static_cast<LONGLONG>(secondOffset - firstOffset);
        pSubMatch->matched = VARIANT_TRUE;
    }

    return S_OK;
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
    MatchEnumerator<IteratorT>& enumerator) noexcept(false)
{
    enumerator.MatchResults().format(
        std::back_inserter(m_outputSink),
        m_formatTemplate,
        m_formatFlags,
        m_regex->GetRegex());
    return S_OK;
}
