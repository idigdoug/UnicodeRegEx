#pragma once
#include "RegExMatchBase.h"

// Iterates over matches in the input. Created by IRegEx::EnumerateMatches.
class RegExMatchEnumerator final : public RegExMatchBase
{
public:

    RegExMatchEnumerator(
        _In_ RegEx* regex,
        _In_ RegExString const* pInput,
        _In_ UINT_PTR startByteOffset,
        RegExMatchFlags matchFlags);

    // IUnknown

    HRESULT STDMETHODCALLTYPE
    QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept override;
};
