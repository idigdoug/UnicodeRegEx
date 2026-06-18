#include "pch.h"
#include "RegEx.h"
#include "RegExMatchEnumerator.h"
#include "RegExMatchResults.h"
#include "InputValidation.h"
#include "MatchEnumerator.h"
#include "OutputSink.h"

RegEx::~RegEx() = default;

RegEx::RegEx(
    Utf16LE::CodePointIterator begin,
    Utf16LE::CodePointIterator end,
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
        auto const chars = Utf16LE().ConvertInPlace(value32);
        hr = AllocBStrFromChars(
            std::span<char16_t const>(reinterpret_cast<char16_t const*>(chars.data()), chars.size()),
            pValue);
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
    RegExBytes input,
    UINT32 inputCodePage,
    LONGLONG startByteOffset,
    RegExMatchFlags flags,
    _Outptr_result_maybenull_ IRegExMatchResults** ppResults) noexcept
{
    return SearchImpl(input, inputCodePage, startByteOffset, flags, true, ppResults);
}

HRESULT
RegEx::Search(
    RegExBytes input,
    UINT32 inputCodePage,
    LONGLONG startByteOffset,
    RegExMatchFlags flags,
    _Outptr_result_maybenull_ IRegExMatchResults** ppResults) noexcept
{
    return SearchImpl(input, inputCodePage, startByteOffset, flags, false, ppResults);
}

HRESULT
RegEx::EnumerateMatches(
    RegExBytes input,
    UINT32 inputCodePage,
    LONGLONG startByteOffset,
    RegExMatchFlags flags,
    _Outptr_ IRegExMatchEnumerator** ppEnumerator) noexcept
{
    HRESULT hr;
    std::unique_ptr<RegExMatchEnumerator> pEnumerator;

    TextEncoding inputEncoding;
    UINT_PTR const startByteOffsetU = static_cast<UINT_PTR>(startByteOffset);
    if (!InputIsValid(input) ||
        !TextEncodingForCodePageIfAligned(inputCodePage, input.data | input.size | startByteOffset, &inputEncoding) ||
        (flags & static_cast<RegExMatchFlags>(boost::match_prev_avail)) ||
        startByteOffsetU > static_cast<UINT_PTR>(input.size))
    {
        hr = E_INVALIDARG;
    }
    else try
    {
        pEnumerator = std::make_unique<RegExMatchEnumerator>(this, input, inputEncoding, startByteOffsetU, flags);
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
    RegExBytes input,
    UINT32 inputCodePage,
    LONGLONG startByteOffset,
    RegExMatchFlags matchFlags,
    _In_ BSTR formatTemplate,
    RegExFormatFlags formatFlags,
    _Out_ BSTR* pOutputString) noexcept
{
    *pOutputString = nullptr;

    auto const flags = static_cast<boost::regex_constants::match_flag_type>(
        static_cast<int>(matchFlags) | static_cast<int>(formatFlags));

    HRESULT hr;
    try
    {
        OutputSink outputSink;
        outputSink.ResetToVector(Utf16LE());
        hr = ReplaceImpl(input, inputCodePage, startByteOffset, formatTemplate, flags, outputSink);
        if (SUCCEEDED(hr))
        {
            auto output = outputSink.FinishVector();
            hr = AllocBStrFromUtf16Bytes(output, pOutputString);
        }
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    return hr;
}

HRESULT
RegEx::ReplaceTo(
    RegExBytes input,
    UINT32 inputCodePage,
    LONGLONG startByteOffset,
    RegExMatchFlags matchFlags,
    _In_ BSTR formatTemplate,
    RegExFormatFlags formatFlags,
    _In_ ISequentialStream* outputStream,
    UINT32 outputCodePage) noexcept
{
    if (outputStream == nullptr)
    {
        return E_POINTER;
    }

    TextEncoding outputEncoding;
    if (!TextEncodingForCodePage(outputCodePage, &outputEncoding))
    {
        return E_INVALIDARG;
    }

    auto const flags = static_cast<boost::regex_constants::match_flag_type>(
        static_cast<int>(matchFlags) | static_cast<int>(formatFlags));

    HRESULT hr;
    try
    {
        OutputSink outputSink;
        outputSink.ResetToStream(outputEncoding, outputStream);
        hr = ReplaceImpl(input, inputCodePage, startByteOffset, formatTemplate, flags, outputSink);
        if (SUCCEEDED(hr))
        {
            outputSink.FinishStream();
        }
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    return hr;
}

HRESULT
RegEx::ReplaceImpl(
    RegExBytes const& input,
    UINT32 inputCodePage,
    LONGLONG startByteOffset,
    _In_ BSTR formatTemplate,
    boost::regex_constants::match_flag_type flags,
    OutputSink& outputSink) const
{
    TextEncoding inputEncoding;
    UINT_PTR const startByteOffsetU = static_cast<UINT_PTR>(startByteOffset);
    if (!InputIsValid(input) ||
        !TextEncodingForCodePageIfAligned(inputCodePage, input.data | input.size | startByteOffset, &inputEncoding) ||
        (flags & boost::match_prev_avail) ||
        startByteOffsetU > static_cast<UINT_PTR>(input.size))
    {
        return E_INVALIDARG;
    }

    auto formatSpan = std::span(reinterpret_cast<char16_t const*>(formatTemplate), SysStringLen(formatTemplate));
    auto formatIterators = Utf16LE().MakeCodePointRange(formatSpan);
    std::u32string const format(formatIterators.begin, formatIterators.end);
    auto inputData = reinterpret_cast<void const*>(static_cast<UINT_PTR>(input.data));
    auto inputSize = static_cast<size_t>(input.size);

    // Driver shared by all encodings: mirrors boost::regex_replace's structure but
    // uses MatchEnumerator so we get the corrected iteration semantics (the same
    // ones IRegExMatchEnumerator exposes). Honors format_no_copy and format_first_only.
    // Bytes in [0, startByteOffset) are not searched but are copied to the output
    // unchanged (unless format_no_copy is set).
    auto runReplace =
        [this, flags, inputData, inputSize, startByteOffsetU, &outputSink, inputEncoding, &format](
            auto encoding)
    {
        using EncodingT = decltype(encoding);
        using IteratorT = EncodingT::CodePointIterator;
        MatchEnumerator<EncodingT> enumerator(*this, flags, inputData, inputSize, startByteOffsetU, encoding);

        bool const noCopy = (flags & boost::regex_constants::format_no_copy) != 0;
        bool const firstOnly = (flags & boost::regex_constants::format_first_only) != 0;

        auto out = std::back_inserter(outputSink);

        // Unmatched ("gap") text is copied to the output untouched. When the input
        // and output encodings match, copy the bytes verbatim so that malformed
        // sequences survive byte-for-byte; otherwise transcode (decode/re-encode)
        // through the sink. Matched text always goes through format() (the
        // modification), which round-trips through char32_t by design.
        auto copyGap = [inputData, inputEncoding, &outputSink](IteratorT const& gapBegin, IteratorT const& gapEnd)
        {
            if (gapBegin != gapEnd)
            {
                auto const* const inputBytes = static_cast<BYTE const*>(inputData);
                size_t const beginOffset = gapBegin.ByteOffset(inputData);
                size_t const endOffset = gapEnd.ByteOffset(inputData);
                auto const gap = std::span<BYTE const>(inputBytes + beginOffset, endOffset - beginOffset);
                if (inputEncoding == outputSink.OutputEncoding())
                {
                    outputSink.AppendRawBytes(gap);
                }
                else
                {
                    outputSink.AppendBytes(gap, inputEncoding);
                }
            }
        };

        // Track where unmatched text continues from. Initialized to the start of the
        // input range (so bytes before startByteOffset are copied to output by the
        // first prefix copy below); updated to match[0].second after each successful match.
        auto tailStart = enumerator.Begin();

        bool found = enumerator.InitialMatch(/*wholeStringMatch*/ false);
        while (found)
        {
            auto const& matchResults = enumerator.MatchResults();
            if (!noCopy)
            {
                // Text between the previous tail position and this match.
                copyGap(tailStart, matchResults[0].first);
            }

            matchResults.format(out, format, flags, m_regex);
            tailStart = matchResults[0].second;
            if (firstOnly)
            {
                break;
            }
            found = enumerator.AdvanceMatch();
        }

        if (!noCopy)
        {
            copyGap(tailStart, enumerator.End());
        }
    };

    std::visit(runReplace, inputEncoding);
    return S_OK;
}

HRESULT
RegEx::SearchImpl(
    RegExBytes const& input,
    UINT32 inputCodePage,
    LONGLONG startByteOffset,
    RegExMatchFlags flags,
    bool wholeStringMatch,
    _Outptr_result_maybenull_ IRegExMatchResults** ppResults) noexcept
{
    HRESULT hr;
    IRegExMatchResults* pResults = nullptr;

    TextEncoding inputEncoding;
    UINT_PTR const startByteOffsetU = static_cast<UINT_PTR>(startByteOffset);
    if (!InputIsValid(input) ||
        !TextEncodingForCodePageIfAligned(inputCodePage, input.data | input.size | startByteOffset, &inputEncoding) ||
        (flags & static_cast<RegExMatchFlags>(boost::match_prev_avail)) ||
        startByteOffsetU > static_cast<UINT_PTR>(input.size))
    {
        hr = E_INVALIDARG;
    }
    else try
    {
        hr = RegExMatchResults::Search(this, input, inputEncoding, startByteOffsetU, flags, wholeStringMatch, &pResults);
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    *ppResults = pResults;
    return hr;
}
