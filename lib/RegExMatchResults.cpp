#include "pch.h"
#include "RegExMatchResults.h"

RegExMatchResults::RegExMatchResults(
    _In_ RegEx* regex,
    RegExBytes const& input,
    TextEncoding inputEncoding,
    _In_ UINT_PTR startByteOffset,
    RegExMatchFlags flags)
    : RegExMatchBase(regex, input, inputEncoding, startByteOffset, flags)
{
}

HRESULT
RegExMatchResults::Search(
    _In_ RegEx* regex,
    RegExBytes const& input,
    TextEncoding inputEncoding,
    _In_ UINT_PTR startByteOffset,
    RegExMatchFlags flags,
    bool wholeStringMatch,
    _Outptr_result_maybenull_ IRegExMatchResults** ppResults) noexcept
{
    *ppResults = nullptr;
    HRESULT hr;

    try
    {
        std::unique_ptr<RegExMatchResults> pResults(
            new RegExMatchResults(regex, input, inputEncoding, startByteOffset, flags));

        if (pResults->DoInitialSearch(wholeStringMatch))
        {
            *ppResults = pResults.release();
        }

        hr = S_OK;
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    return hr;
}

HRESULT __stdcall
RegExMatchResults::QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept
{
    if (ppvObject == nullptr)
    {
        return E_POINTER;
    }

    if (riid == __uuidof(IUnknown) ||
        riid == __uuidof(IRegExMatchResults))
    {
        *ppvObject = static_cast<IRegExMatchResults*>(static_cast<IRegExMatchEnumerator*>(this));
        AddRef();
        return S_OK;
    }

    *ppvObject = nullptr;
    return E_NOINTERFACE;
}

HRESULT
RegExMatchResults::NextMatch(_Out_ VARIANT_BOOL* pFound) noexcept
{
    // Unreachable.
    *pFound = VARIANT_FALSE;
    return E_NOTIMPL;
}
