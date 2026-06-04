#include "pch.h"
#include "RegEx.h"
#include "RegExMatchEnumerator.h"
#include "RegExMatchResults.h"

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
    _In_ RegExString const* pInput,
    _In_ LONGLONG startByteOffset,
    RegExMatchFlags flags,
    _Outptr_opt_result_maybenull_ IRegExMatchResults** ppResults) noexcept
{
    return Search(pInput, startByteOffset, flags, true, ppResults);
}

HRESULT
RegEx::Search(
    _In_ RegExString const* pInput,
    _In_ LONGLONG startByteOffset,
    RegExMatchFlags flags,
    _Outptr_opt_result_maybenull_ IRegExMatchResults** ppResults) noexcept
{
    return Search(pInput, startByteOffset, flags, false, ppResults);
}

HRESULT
RegEx::EnumerateMatches(
    _In_ RegExString const* pInput,
    _In_ LONGLONG startByteOffset,
    RegExMatchFlags flags,
    _Outptr_ IRegExMatchEnumerator** ppEnumerator) noexcept
{
    HRESULT hr;
    std::unique_ptr<RegExMatchEnumerator> pEnumerator;
    UINT_PTR startOffsetU = static_cast<UINT_PTR>(startByteOffset);

    if (flags & static_cast<RegExMatchFlags>(boost::match_prev_avail))
    {
        hr = E_INVALIDARG;
    }
    else if (startOffsetU > static_cast<UINT_PTR>(pInput->size))
    {
        hr = E_INVALIDARG;
    }
    else try
    {
        pEnumerator = std::make_unique<RegExMatchEnumerator>(this, pInput, startOffsetU, flags);
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
RegEx::Search(
    _In_ RegExString const* pInput,
    _In_ LONGLONG startByteOffset,
    RegExMatchFlags flags,
    bool wholeStringMatch,
    _Outptr_opt_result_maybenull_ IRegExMatchResults** ppResults) noexcept
{
    HRESULT hr;
    IRegExMatchResults* pResults = nullptr;
    UINT_PTR startOffsetU = static_cast<UINT_PTR>(startByteOffset);

    if (flags & static_cast<RegExMatchFlags>(boost::match_prev_avail))
    {
        hr = E_INVALIDARG;
    }
    else if (startOffsetU > static_cast<UINT_PTR>(pInput->size))
    {
        hr = E_INVALIDARG;
    }
    else try
    {
        hr = RegExMatchResults::Search(this, pInput, startOffsetU, flags, wholeStringMatch, &pResults);
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    *ppResults = pResults;
    return hr;
}
