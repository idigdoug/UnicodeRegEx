#include "pch.h"
#include "RegExLibrary.h"
#include "RegEx.h"
#include "RegExFileStream.h"
#include "RegExMemoryStream.h"
#include "OutputSink.h"
#include "InputTranscoder.h"

#include <utf.h>

using namespace std::string_view_literals;

STDAPI
RepStrRegExLibraryCreate(
    _Outptr_ IRegExLibrary** ppLibrary)
{
    if (ppLibrary == nullptr)
    {
        return E_POINTER;
    }

    try
    {
        *ppLibrary = new RegExLibrary();
        return S_OK;
    }
    catch (std::bad_alloc const&)
    {
        *ppLibrary = nullptr;
        return E_OUTOFMEMORY;
    }
}

RegExLibrary::RegExLibrary() noexcept
    : m_refCount(1)
{
}

HRESULT STDMETHODCALLTYPE
RegExLibrary::QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept
{
    if (ppvObject == nullptr)
    {
        return E_POINTER;
    }

    if (riid == __uuidof(IUnknown) || riid == __uuidof(IRegExLibrary))
    {
        *ppvObject = static_cast<IRegExLibrary*>(this);
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

ULONG STDMETHODCALLTYPE
RegExLibrary::AddRef() noexcept
{
    return InterlockedIncrementNoFence(&m_refCount);
}

ULONG STDMETHODCALLTYPE
RegExLibrary::Release() noexcept
{
    ULONG ref = InterlockedDecrementRelease(&m_refCount);
    if (ref == 0)
    {
        delete this;
    }

    return ref;
}

HRESULT STDMETHODCALLTYPE
RegExLibrary::CreateRegEx(
    _In_ BSTR pattern,
    RegExSyntaxFlags syntaxFlags,
    UINT32 lcid,
    _Out_ RegExErrorCode* pErrorCode,
    _Outptr_ IRegEx** ppRegEx) noexcept
{
    std::unique_ptr<RegEx> pRegEx;
    HRESULT hr;
    boost::regex_constants::error_type errorCode;
    PCSTR errorMessage = nullptr;

    if (syntaxFlags & static_cast<RegExSyntaxFlags>(boost::regex_constants::no_except))
    {
        hr = E_INVALIDARG;
        errorCode = boost::regex_constants::error_unknown;
    }
    else try
    {
        static_assert(sizeof(pattern[0]) == sizeof(char16_t), "BSTR must be UTF-16");
        auto patternIterators = utf16le::CodePointIterator::FromSpan(std::span(
            reinterpret_cast<char16_t const*>(pattern),
            SysStringLen(pattern)));
        pRegEx = std::make_unique<RegEx>(
            patternIterators.begin,
            patternIterators.end,
            static_cast<boost::regex_constants::syntax_option_type>(syntaxFlags),
            lcid);
        hr = S_OK;
        errorCode = boost::regex_constants::error_ok;
    }
    catch (boost::regex_error const& ex)
    {
        hr = MK_E_SYNTAX;
        errorCode = ex.code();
        errorMessage = WindowsChar32RegexTraits().error_string(errorCode);
    }
    catch (std::bad_alloc const&)
    {
        hr = E_OUTOFMEMORY;
        errorCode = boost::regex_constants::error_unknown;
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
        errorCode = boost::regex_constants::error_unknown;
    }

    auto setErrorInfo = false;
    if (errorMessage)
    {
        // Convert ASCII error message to BSTR.
        unsigned cch = static_cast<unsigned>(strnlen(errorMessage, 512));
        wil::unique_bstr errorMessageBstr(SysAllocStringLen(nullptr, cch));
        if (errorMessageBstr)
        {
            for (unsigned i = 0; i != cch; i += 1)
            {
                errorMessageBstr.get()[i] = errorMessage[i];
            }

            wil::com_ptr<ICreateErrorInfo> createErrorInfo;
            if (SUCCEEDED(CreateErrorInfo(createErrorInfo.put())) &&
                SUCCEEDED(createErrorInfo->SetDescription(errorMessageBstr.get())))
            {
                auto errorInfo = createErrorInfo.try_query<IErrorInfo>();
                if (errorInfo && SUCCEEDED(SetErrorInfo(0, errorInfo.get())))
                {
                    setErrorInfo = true;
                }
            }
        }
    }

    if (!setErrorInfo)
    {
        // If we failed to set the error info with the error message, clear any existing error info.
        SetErrorInfo(0, nullptr);
    }

    if (pErrorCode)
    {
        *pErrorCode = static_cast<RegExErrorCode>(errorCode);
    }

    *ppRegEx = pRegEx.release();
    return hr;
}

// Append '\' before each input wide character that matches (as ASCII) one of the
// characters in charsToEscape, and produce the result as a freshly-allocated BSTR.
// If charsToEscape is empty, returns a copy of the input with no escaping.
// On allocation failure, sets *pOutput = nullptr and returns E_OUTOFMEMORY.
static HRESULT
EscapeAsciiSpecials(
    _In_ BSTR input,
    std::string_view charsToEscape,
    _Out_ BSTR* pOutput) noexcept
{
    *pOutput = nullptr;

    std::wstring_view inputView(input, SysStringLen(input));
    std::wstring_view outputView;
    std::wstring output;
    bool escape[128] = {};

    if (charsToEscape.empty())
    {
        outputView = inputView;
    }
    else
    {
        for (char ch : charsToEscape)
        {
            escape[static_cast<unsigned char>(ch)] = true;
        }

        try
        {
            output.reserve(inputView.size() * 2); // Worst case, every character needs escaping.
            for (wchar_t ch : inputView)
            {
                if (ch < 128 && escape[static_cast<unsigned char>(ch)])
                {
                    output.push_back(L'\\');
                }
                output.push_back(ch);
            }
        }
        catch (std::bad_alloc const&)
        {
            return E_OUTOFMEMORY;
        }

        outputView = output;
    }

    *pOutput = SysAllocStringLen(outputView.data(), static_cast<UINT>(outputView.size()));
    return *pOutput ? S_OK : E_OUTOFMEMORY;
}

HRESULT STDMETHODCALLTYPE
RegExLibrary::EscapePatternLiteral(
    _In_ BSTR patternLiteral,
    RegExSyntaxFlags syntaxFlags,
    _Out_ BSTR* pEscapedPatternLiteral) noexcept
{
    if (!pEscapedPatternLiteral)
    {
        return E_POINTER;
    }

    std::string_view charsToEscape{};
    auto const flags = static_cast<boost::regex_constants::syntax_option_type>(syntaxFlags);
    switch (flags & boost::regbase::main_option_type)
    {
    case boost::regbase::perl_syntax_group:
        // Perl engine: extended, normal, awk, egrep, perl, ECMAScript, JavaScript, JScript.
        charsToEscape = R"(.[{}()\*+?|^$)"sv;
        break;
    case boost::regbase::basic_syntax_group:
        // Basic engine: basic, emacs, grep, sed.
        charsToEscape = R"(.[\*^$)"sv;
        break;
    case boost::regbase::literal:
        // No escaping necessary.
        break;
    default:
        // Both PERL and BASIC set at the same time.
        *pEscapedPatternLiteral = nullptr;
        return E_INVALIDARG;
    }

    return EscapeAsciiSpecials(patternLiteral, charsToEscape, pEscapedPatternLiteral);
}

HRESULT STDMETHODCALLTYPE
RegExLibrary::EscapeFormatLiteral(
    _In_ BSTR formatLiteral,
    RegExFormatFlags formatFlags,
    _Out_ BSTR* pEscapedFormatLiteral) noexcept
{
    constexpr int FormatPerl = boost::regex_constants::format_perl;
    static_assert(FormatPerl == 0, "FormatPerl is expected to be the lack of FormatSed");
    constexpr int FormatSed = boost::regex_constants::format_sed;
    static_assert(0 == (FormatSed & (FormatSed - 1)), "FormatSed is expected to be a single bit flag");
    constexpr int FormatAll = boost::regex_constants::format_all;
    static_assert(0 == (FormatAll & (FormatAll - 1)), "FormatAll is expected to be a single bit flag");
    constexpr int FormatMask = FormatSed | FormatAll;

    if (!pEscapedFormatLiteral)
    {
        return E_POINTER;
    }

    std::string_view charsToEscape{};
    auto const flags = static_cast<boost::regex_constants::match_flag_type>(formatFlags);
    if (!(flags & boost::regex_constants::format_literal))
    {
        switch (flags & FormatMask)
        {
        case FormatPerl:
            charsToEscape = R"($\)"sv;
            break;
        case FormatPerl | FormatAll:
            charsToEscape = R"($\()?:)"sv;
            break;
        case FormatSed:
            charsToEscape = R"(&\)"sv;
            break;
        case FormatSed | FormatAll:
            charsToEscape = R"(&\()?:)"sv;
            break;
        default:
            // FormatMask allows 2 bits through. Above set of cases is exhaustive.
            assert(false);
            __assume(false);
        }
    }

    return EscapeAsciiSpecials(formatLiteral, charsToEscape, pEscapedFormatLiteral);
}

HRESULT
RegExLibrary::Transcode(
    _In_ RegExBytes const* pInput,
    RegExEncoding inputEncoding,
    _Out_ BSTR* pOutput) noexcept
{
    *pOutput = nullptr;

    if (!InputTranscoder::OffsetAndSizeAreAlignedForEncoding(pInput->data_ptr, pInput->size, inputEncoding))
    {
        return E_INVALIDARG;
    }

    HRESULT hr;
    try
    {
        std::span<BYTE const> input(
            reinterpret_cast<BYTE const*>(static_cast<UINT_PTR>(pInput->data_ptr)),
            static_cast<size_t>(pInput->size));

        std::span<BYTE const> output;
        OutputSink sink;
        if (inputEncoding == RegExEncoding_utf16le)
        {
            output = input;
        }
        else
        {
            sink.ResetToVector(RegExEncoding_utf16le);
            sink.AppendBytes(input, inputEncoding);
            output = sink.FinishVector();
        }

        *pOutput = SysAllocStringLen(
            reinterpret_cast<OLECHAR const*>(output.data()),
            static_cast<UINT>(output.size() / sizeof(OLECHAR)));
        hr = *pOutput ? S_OK : E_OUTOFMEMORY;
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    return hr;
}

HRESULT
RegExLibrary::TranscodeTo(
    _In_ RegExBytes const* pInput,
    RegExEncoding inputEncoding,
    RegExEncoding outputEncoding,
    _In_ ISequentialStream* outputStream) noexcept
{
    if (outputStream == nullptr)
    {
        return E_POINTER;
    }
    else if (
        !RegExEncodingIsValid(outputEncoding) ||
        !InputTranscoder::OffsetAndSizeAreAlignedForEncoding(pInput->data_ptr, pInput->size, inputEncoding))
    {
        return E_INVALIDARG;
    }

    HRESULT hr;
    try
    {
        std::span<BYTE const> input(
            reinterpret_cast<BYTE const*>(static_cast<UINT_PTR>(pInput->data_ptr)),
            static_cast<size_t>(pInput->size));

        if (inputEncoding == outputEncoding)
        {
            hr = WriteAllBytesToStream(outputStream, input);
        }
        else
        {
            OutputSink sink;
            sink.ResetToStream(outputEncoding, outputStream);
            sink.AppendBytes(input, inputEncoding);
            sink.FinishStream();
            hr = S_OK;
        }
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    return hr;
}

HRESULT STDMETHODCALLTYPE
RegExLibrary::CreateMemoryStream(
    LONGLONG initialCapacity,
    _Outptr_ IRegExMemoryStream** ppStream) noexcept
{
    if (ppStream == nullptr)
    {
        return E_POINTER;
    }

    *ppStream = nullptr;

    if (initialCapacity < 0)
    {
        return E_INVALIDARG;
    }

    try
    {
        *ppStream = new RegExMemoryStream(initialCapacity);
        return S_OK;
    }
    catch (...)
    {
        return wil::ResultFromCaughtException();
    }
}

HRESULT STDMETHODCALLTYPE
RegExLibrary::CreateFileStream(
    _In_ BSTR path,
    RegExFileStreamFlags flags,
    _Outptr_ IRegExFileStream** ppStream) noexcept
{
    if (ppStream == nullptr)
    {
        return E_POINTER;
    }

    *ppStream = nullptr;

    // Use wcsnlen, not SysStringLen, to avoid trouble with embedded NULs.
    size_t const length = wcsnlen(path, SysStringLen(path));
    if (length == 0)
    {
        return E_INVALIDARG;
    }

    try
    {
        *ppStream = new RegExFileStream(std::wstring(path, length), flags);
        return S_OK;
    }
    catch (...)
    {
        return wil::ResultFromCaughtException();
    }
}

HRESULT STDMETHODCALLTYPE
RegExLibrary::CreateReplacementFileStream(
    _In_ BSTR finalPath,
    _Outptr_ IRegExFileStream** ppStream) noexcept
{
    if (ppStream == nullptr)
    {
        return E_POINTER;
    }

    *ppStream = nullptr;

    // Use wcsnlen, not SysStringLen, to avoid trouble with embedded NULs.
    size_t const finalLen = wcsnlen(finalPath, SysStringLen(finalPath));
    if (finalLen == 0)
    {
        return E_INVALIDARG;
    }

    HRESULT hr = E_FAIL;
    wil::com_ptr<RegExFileStream> stream;
    try
    {
        // Build a sibling temp filename: "<finalPath>.<8-hex>.tmp".
        std::wstring tempPath;
        tempPath.reserve(finalLen + 16);

        constexpr unsigned MaxAttempts = 8;
        for (unsigned attempt = 0; attempt < MaxAttempts; attempt += 1)
        {
            // Combine a few low-precision sources for a random-enough suffix.
            UINT32 const random =
                static_cast<UINT32>(GetTickCount64() & 0xFFFFFFFFu) ^
                (GetCurrentThreadId() * 0x9E3779B9u) ^
                (attempt << 16);

            wchar_t suffix[24];
            swprintf_s(suffix, L".%08X.tmp", random);

            tempPath.assign(finalPath, finalLen);
            tempPath.append(suffix);

            auto const flags = static_cast<RegExFileStreamFlags>(
                RegExFileStreamFlag_create_new |
                RegExFileStreamFlag_delete_on_close |
                RegExFileStreamFlag_sequential);

            try
            {
                stream.attach(new RegExFileStream(std::move(tempPath), flags));
                hr = S_OK;
                break;
            }
            catch (wil::ResultException const& ex)
            {
                hr = ex.GetErrorCode();
                if (hr != HRESULT_FROM_WIN32(ERROR_FILE_EXISTS) &&
                    hr != HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS))
                {
                    break;
                }

                // Have a consistent error code if we run out of retries.
                hr = HRESULT_FROM_WIN32(ERROR_FILE_EXISTS);
            }

            // collision: retry with a new random suffix
        }
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    *ppStream = stream.detach();
    return hr;
}
