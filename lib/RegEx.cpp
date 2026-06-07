#include "pch.h"
#include "RegEx.h"
#include "RegExMatchEnumerator.h"
#include "RegExMatchResults.h"
#include "InputTranscoder.h"
#include "OutputSink.h"

#include <utf.h>

RegEx::~RegEx() = default;

RegEx::RegEx(
    utf16le::CodePointIterator begin,
    utf16le::CodePointIterator end,
    boost::regex_constants::syntax_option_type flags,
    UINT32 lcid)
    : m_refCount(1)
    , m_regex()
    , m_freeThreadedMarshaler()
{
    if (lcid != m_regex.getloc())
    {
        m_regex.imbue(lcid);
    }

    m_regex.assign(begin, end, flags);
}

boost::basic_regex<char32_t, WindowsChar32RegexTraits> const&
RegEx::GetRegex() const noexcept
{
    return m_regex;
}

HRESULT __stdcall
RegEx::QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept
{
    if (ppvObject == nullptr)
    {
        return E_POINTER;
    }

    if (riid == __uuidof(IUnknown) || riid == __uuidof(IRegEx))
    {
        *ppvObject = static_cast<IRegEx*>(this);
        AddRef();
        return S_OK;
    }

    if (riid == __uuidof(IMarshal))
    {
        if (!m_freeThreadedMarshaler)
        {
            // Delay-create the FTM, mainly so we work ok with pseudo-COM environments that
            // don't initialize COM.
            wil::com_ptr<IUnknown> freeThreadedMarshaler;
            RETURN_IF_FAILED(CoCreateFreeThreadedMarshaler(this, freeThreadedMarshaler.put()));
            if (nullptr == InterlockedCompareExchangePointer(
                reinterpret_cast<void**>(m_freeThreadedMarshaler.addressof()),
                freeThreadedMarshaler.get(),
                nullptr))
            {
                (void)freeThreadedMarshaler.detach();
            }
        }

        return m_freeThreadedMarshaler->QueryInterface(riid, ppvObject);
    }

    *ppvObject = nullptr;
    return E_NOINTERFACE;
}

ULONG __stdcall
RegEx::AddRef() noexcept
{
    return InterlockedIncrementNoFence(&m_refCount);
}

ULONG __stdcall
RegEx::Release() noexcept
{
    ULONG ref = InterlockedDecrementRelease(&m_refCount);
    if (ref == 0)
    {
        delete this;
    }

    return ref;
}

HRESULT
RegEx::get_Pattern(
    _Out_ BSTR* pValue) noexcept
{
    HRESULT hr;

    try
    {
        auto value32 = m_regex.str();
        auto const chars = utf16le::ConvertInPlace(value32);
        static_assert(sizeof(chars[0]) == sizeof(OLECHAR), "OLECHAR must be UTF-16");
        auto const value = SysAllocStringLen(
            reinterpret_cast<OLECHAR const*>(chars.data()),
            static_cast<UINT>(chars.size()));
        *pValue = value;
        hr = value ? S_OK : E_OUTOFMEMORY;
    }
    catch (std::bad_alloc const&)
    {
        *pValue = nullptr;
        hr = E_OUTOFMEMORY;
    }
    catch (...)
    {
        *pValue = nullptr;
        hr = wil::ResultFromCaughtException();
    }

    return hr;
}

HRESULT
RegEx::get_Flags(
    _Out_ RegExSyntaxFlags* pValue) noexcept
{
    *pValue = static_cast<RegExSyntaxFlags>(m_regex.flags());
    return S_OK;
}

HRESULT
RegEx::get_Lcid(
    _Out_ UINT32* pValue) noexcept
{
    *pValue = m_regex.getloc();
    return S_OK;
}

HRESULT
RegEx::Match(
    _In_ RegExBytes const* pInput,
    RegExEncoding inputEncoding,
    LONGLONG startByteOffset,
    RegExMatchFlags flags,
    _Outptr_result_maybenull_ IRegExMatchResults** ppResults) noexcept
{
    return SearchImpl(pInput, inputEncoding, startByteOffset, flags, true, ppResults);
}

HRESULT
RegEx::Search(
    _In_ RegExBytes const* pInput,
    RegExEncoding inputEncoding,
    LONGLONG startByteOffset,
    RegExMatchFlags flags,
    _Outptr_result_maybenull_ IRegExMatchResults** ppResults) noexcept
{
    return SearchImpl(pInput, inputEncoding, startByteOffset, flags, false, ppResults);
}

HRESULT
RegEx::EnumerateMatches(
    _In_ RegExBytes const* pInput,
    RegExEncoding inputEncoding,
    LONGLONG startByteOffset,
    RegExMatchFlags flags,
    _Outptr_ IRegExMatchEnumerator** ppEnumerator) noexcept
{
    HRESULT hr;
    std::unique_ptr<RegExMatchEnumerator> pEnumerator;
    UINT_PTR startOffsetU = static_cast<UINT_PTR>(startByteOffset);

    if (!RegExEncodingIsValid(inputEncoding))
    {
        hr = E_INVALIDARG;
    }
    else if (flags & static_cast<RegExMatchFlags>(boost::match_prev_avail))
    {
        hr = E_INVALIDARG;
    }
    else if (startOffsetU > static_cast<UINT_PTR>(pInput->size))
    {
        hr = E_INVALIDARG;
    }
    else try
    {
        pEnumerator = std::make_unique<RegExMatchEnumerator>(this, pInput, inputEncoding, startOffsetU, flags);
        hr = S_OK;
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    *ppEnumerator = pEnumerator.release();
    return hr;
}

HRESULT
RegEx::Replace(
    _In_ RegExBytes const* pInput,
    RegExEncoding inputEncoding,
    RegExMatchFlags matchFlags,
    _In_ BSTR formatTemplate,
    RegExFormatFlags formatFlags,
    _Out_ BSTR* pOutputString) noexcept
{
    *pOutputString = nullptr;

    auto const flags = static_cast<boost::regex_constants::match_flag_type>(
        static_cast<int>(matchFlags) | static_cast<int>(formatFlags));
    if (!InputTranscoder::OffsetAndSizeAreAlignedForEncoding(pInput->data_ptr, pInput->size, inputEncoding) ||
        (flags & boost::match_prev_avail))
    {
        return E_INVALIDARG;
    }

    HRESULT hr;
    try
    {
        OutputSink outputSink;
        outputSink.ResetToVector(RegExEncoding_utf16le);
        ReplaceImpl(pInput, inputEncoding, formatTemplate, flags, outputSink);
        auto output = outputSink.FinishVector();
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
RegEx::ReplaceTo(
    _In_ RegExBytes const* pInput,
    RegExEncoding inputEncoding,
    RegExMatchFlags matchFlags,
    _In_ BSTR formatTemplate,
    RegExFormatFlags formatFlags,
    RegExEncoding outputEncoding,
    _In_ ISequentialStream* outputStream) noexcept
{
    if (outputStream == nullptr)
    {
        return E_POINTER;
    }

    auto const flags = static_cast<boost::regex_constants::match_flag_type>(
        static_cast<int>(matchFlags) | static_cast<int>(formatFlags));
    if (!RegExEncodingIsValid(outputEncoding) ||
        !InputTranscoder::OffsetAndSizeAreAlignedForEncoding(pInput->data_ptr, pInput->size, inputEncoding) ||
        (flags & boost::match_prev_avail))
    {
        return E_INVALIDARG;
    }

    HRESULT hr;
    try
    {
        OutputSink outputSink;
        outputSink.ResetToStream(outputEncoding, outputStream);
        ReplaceImpl(pInput, inputEncoding, formatTemplate, flags, outputSink);
        outputSink.FinishStream();
        hr = S_OK;
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    return hr;
}

void
RegEx::ReplaceImpl(
    _In_ RegExBytes const* pInput,
    RegExEncoding inputEncoding,
    _In_ BSTR formatTemplate,
    boost::regex_constants::match_flag_type flags,
    OutputSink& outputSink) const
{
    auto formatSpan = std::span(reinterpret_cast<char16_t const*>(formatTemplate), SysStringLen(formatTemplate));
    auto formatIterators = utf16le::CodePointIterator::FromSpan(formatSpan);
    std::u32string const format(formatIterators.begin, formatIterators.end);
    auto inputData = reinterpret_cast<void const*>(static_cast<UINT_PTR>(pInput->data_ptr));
    auto inputSize = static_cast<size_t>(pInput->size);

    switch (inputEncoding)
    {
    case RegExEncoding_latin1:
    {
        auto input = latin1::CodePointIterator::FromSpan(
            std::span(static_cast<char const*>(inputData), inputSize / sizeof(char)));
        boost::regex_replace(std::back_inserter(outputSink), input.begin, input.end, m_regex, format, flags);
        break;
    }
    case RegExEncoding_utf8:
    {
        auto input = utf8::CodePointIterator::FromSpan(
            std::span(static_cast<char8_t const*>(inputData), inputSize / sizeof(char8_t)));
        boost::regex_replace(std::back_inserter(outputSink), input.begin, input.end, m_regex, format, flags);
        break;
    }
    case RegExEncoding_utf16le:
    {
        auto input = utf16le::CodePointIterator::FromSpan(
            std::span(static_cast<char16_t const*>(inputData), inputSize / sizeof(char16_t)));
        boost::regex_replace(std::back_inserter(outputSink), input.begin, input.end, m_regex, format, flags);
        break;
    }
    case RegExEncoding_utf16be:
    {
        auto input = utf16be::CodePointIterator::FromSpan(
            std::span(static_cast<char16_t const*>(inputData), inputSize / sizeof(char16_t)));
        boost::regex_replace(std::back_inserter(outputSink), input.begin, input.end, m_regex, format, flags);
        break;
    }
    default:
        assert(false); // Checked by caller.
        break;
    }
}

HRESULT
RegEx::SearchImpl(
    _In_ RegExBytes const* pInput,
    RegExEncoding inputEncoding,
    LONGLONG startByteOffset,
    RegExMatchFlags flags,
    bool wholeStringMatch,
    _Outptr_result_maybenull_ IRegExMatchResults** ppResults) noexcept
{
    HRESULT hr;
    IRegExMatchResults* pResults = nullptr;
    UINT_PTR startOffsetU = static_cast<UINT_PTR>(startByteOffset);

    if (!RegExEncodingIsValid(inputEncoding) ||
        (flags & static_cast<RegExMatchFlags>(boost::match_prev_avail)) ||
        startOffsetU > static_cast<UINT_PTR>(pInput->size))
    {
        hr = E_INVALIDARG;
    }
    else try
    {
        hr = RegExMatchResults::Search(this, pInput, inputEncoding, startOffsetU, flags, wholeStringMatch, &pResults);
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    *ppResults = pResults;
    return hr;
}
