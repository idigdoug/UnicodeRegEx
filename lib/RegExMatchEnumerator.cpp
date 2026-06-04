#include "pch.h"
#include "RegExMatchEnumerator.h"

RegExMatchEnumerator::RegExMatchEnumerator(
    _In_ RegEx* regex,
    _In_ RegExString const* pInput,
    _In_ UINT_PTR startByteOffset,
    RegExMatchFlags flags)
    : RegExMatchBase(regex, pInput, startByteOffset, flags)
{
}

HRESULT __stdcall
RegExMatchEnumerator::QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept
{
    if (ppvObject == nullptr)
    {
        return E_POINTER;
    }

    if (riid == __uuidof(IUnknown) ||
        riid == __uuidof(IRegExMatchResults) ||
        riid == __uuidof(IRegExMatchEnumerator))
    {
        *ppvObject = static_cast<IRegExMatchEnumerator*>(this);
        AddRef();
        return S_OK;
    }

    *ppvObject = nullptr;
    return E_NOINTERFACE;
}
