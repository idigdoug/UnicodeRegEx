#include "pch.h"
#include "RegEx.h"
#include "RegExMatchEnumerator.h"

#include <utf.h>

STDAPI
RepStrRegExCreate(
    _In_ BSTR pattern,
    RegExSyntaxFlags syntaxFlags,
    UINT32 lcid,
    _Out_opt_ RegExErrorCode* pErrorCode,
    _Outptr_ IRegEx** ppRegEx)
{
    std::unique_ptr<RegEx> pRegEx;
    HRESULT hr;
    boost::regex_constants::error_type errorCode;
    PCSTR errorMessage = nullptr;

    try
    {
        static_assert(sizeof(pattern[0]) == sizeof(char16_t), "BSTR must be UTF-16");
        auto patternIterators = utf16le::CodePointIterator::FromSpan(std::span(
            reinterpret_cast<char16_t const*>(pattern),
            SysStringLen(pattern)));
        pRegEx = std::make_unique<RegEx>(
            patternIterators.first,
            patternIterators.second,
            static_cast<boost::regex_constants::syntax_option_type>(syntaxFlags),
            lcid);
        hr = S_OK;
        errorCode = boost::regex_constants::error_ok;
    }
    catch (boost::regex_error const& ex)
    {
        hr = E_INVALIDARG;
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
RegEx::CreateMatchEnumerator(
    _In_ RegExString const* pInput,
    RegExMatchFlags flags,
    _Outptr_ IRegExMatchEnumerator** ppEnumerator) noexcept
{
    HRESULT hr;
    std::unique_ptr<RegExMatchEnumerator> pEnumerator;

    try
    {
        pEnumerator = std::make_unique<RegExMatchEnumerator>(this, pInput, flags);
        hr = S_OK;
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    *ppEnumerator = pEnumerator.release();
    return hr;
}
