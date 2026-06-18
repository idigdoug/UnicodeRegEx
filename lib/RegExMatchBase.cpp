#include "pch.h"
#include "RegExMatchBase.h"
#include "RegEx.h"
#include "InputValidation.h"

#include <TextEncoding.h>

static constexpr RegExBytes
MakeString(_In_reads_bytes_(size) void const* data, UINT_PTR size) noexcept
{
    return {
        .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(data)),
        .size = static_cast<LONGLONG>(size),
    };
}

RegExMatchBase::VariantEnumerator
RegExMatchBase::SelectEnumerator(
    TextEncoding inputEncoding,
    RegEx const& regex,
    RegExMatchFlags flags,
    void const* inputData,
    size_t inputSize,
    UINT_PTR startByteOffset)
{
    auto const matchFlags = static_cast<boost::regex_constants::match_flag_type>(flags);
    return std::visit([&regex, matchFlags, inputData, inputSize, startByteOffset](auto encoding) {
            using EncodingT = decltype(encoding);
            return VariantEnumerator(
                std::in_place_type<MatchEnumerator<EncodingT>>,
                regex, matchFlags, inputData, inputSize, startByteOffset, encoding);
        },
        inputEncoding);

}

RegExMatchBase::~RegExMatchBase() = default;

RegExMatchBase::RegExMatchBase(
    _In_ RegEx* regex,
    RegExBytes const& input,
    TextEncoding inputEncoding,
    UINT_PTR startByteOffset,
    RegExMatchFlags flags)
    : m_refCount(1)
    , m_regex(regex)
    , m_inputData(reinterpret_cast<void const*>(static_cast<UINT_PTR>(input.data)))
    , m_inputSize(static_cast<UINT_PTR>(input.size))
    , m_inputEncoding(inputEncoding)
    , m_state(RegExEnumerationState_not_started)
    , m_variantEnumerator(SelectEnumerator(inputEncoding, *regex, flags, m_inputData, m_inputSize, startByteOffset))
    , m_outputSink()
    , m_formatTemplate()
    , m_formatFlags(boost::regex_constants::format_default)
{
}

bool
RegExMatchBase::DoInitialSearch(bool wholeStringMatch)
{
    assert(m_state == RegExEnumerationState_not_started);

    bool found = std::visit([wholeStringMatch](auto& enumerator) {
            return enumerator.InitialMatch(wholeStringMatch);
        },
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
RegExMatchBase::get_InputCodePage(_Out_ UINT32* pCodePage) noexcept
{
    *pCodePage = std::visit([](auto encoding) {
            return encoding.CodePage();
        },
        m_inputEncoding);
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
        *pCount = std::visit([](auto& enumerator) {
                return static_cast<UINT32>(enumerator.MatchResults().size());
            },
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
        hr = std::visit([this, subMatchIndex, pSubMatch](auto& enumerator) {
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
            },
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
        auto formatIterators = Utf16LE().MakeCodePointRange(std::span(
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
            m_outputSink.ResetToVector(Utf16LE());

            std::visit([this](auto& enumerator) {
                    enumerator.MatchResults().format(
                        std::back_inserter(m_outputSink),
                        m_formatTemplate,
                        m_formatFlags,
                        m_regex->GetRegex());
                },
                m_variantEnumerator);

            auto bytes = m_outputSink.FinishVector();
            hr = AllocBStrFromUtf16Bytes(bytes, pOutputString);
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
    UINT32 outputCodePage) noexcept
{
    HRESULT hr;
    TextEncoding outputEncoding;

    if (outputStream == nullptr)
    {
        hr = E_POINTER;
    }
    else if (!TextEncodingForCodePage(outputCodePage, &outputEncoding))
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

            std::visit([this](auto& enumerator) {
                    enumerator.MatchResults().format(
                        std::back_inserter(m_outputSink),
                        m_formatTemplate,
                        m_formatFlags,
                        m_regex->GetRegex());
                },
                m_variantEnumerator);

            m_outputSink.FinishStream();
            hr = S_OK;
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

    if (!RangeIsInBounds(inputOffset, size, m_inputSize) ||
        !InputIsAligned(m_inputEncoding, inputOffset | size))
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
        if (std::holds_alternative<Utf16LE>(m_inputEncoding))
        {
            output = input;
        }
        else
        {
            m_outputSink.ResetToVector(Utf16LE());
            m_outputSink.AppendBytes(input, m_inputEncoding);
            output = m_outputSink.FinishVector();
        }

        hr = AllocBStrFromUtf16Bytes(output, pOutputString);
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
    UINT32 outputCodePage) noexcept
{
    TextEncoding outputEncoding;

    if (outputStream == nullptr)
    {
        return E_POINTER;
    }
    else if (
        !TextEncodingForCodePage(outputCodePage, &outputEncoding) ||
        !RangeIsInBounds(inputOffset, size, m_inputSize) ||
        !InputIsAligned(m_inputEncoding, inputOffset | size))
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
            std::visit([this](auto& enumerator) {
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
                },
                m_variantEnumerator);

            found = m_state == RegExEnumerationState_enumerating;
            hr = S_OK;
        }
        catch (...)
        {
            hr = wil::ResultFromCaughtException();
        }
    }

    *pFound = found ? VARIANT_TRUE : VARIANT_FALSE;
    return hr;
}
