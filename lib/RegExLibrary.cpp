#include "pch.h"
#include "RegExLibrary.h"
#include "RegEx.h"

#include <utf.h>

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

HRESULT STDMETHODCALLTYPE
RegExLibrary::EscapePatternLiteral(
    _In_ BSTR /*patternLiteral*/,
    RegExSyntaxFlags /*syntaxFlags*/,
    _Out_ BSTR* pEscapedPatternLiteral) noexcept
{
    if (pEscapedPatternLiteral)
    {
        *pEscapedPatternLiteral = nullptr;
    }
    return E_NOTIMPL;
}

HRESULT STDMETHODCALLTYPE
RegExLibrary::EscapeFormatLiteral(
    _In_ BSTR /*formatLiteral*/,
    RegExFormatFlags /*formatFlags*/,
    _Out_ BSTR* pEscapedFormatLiteral) noexcept
{
    if (pEscapedFormatLiteral)
    {
        *pEscapedFormatLiteral = nullptr;
    }
    return E_NOTIMPL;
}
