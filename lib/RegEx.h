#pragma once
#include <RepStrRegEx.h>
#include <WindowsChar32RegexTraits.h>
#include <utf.h>

class RegEx final : public IRegEx
{
    volatile long m_refCount;
    boost::basic_regex<char32_t, WindowsChar32RegexTraits> m_regex;
    wil::com_ptr<IUnknown> m_freeThreadedMarshaler;

public:

    ~RegEx();

    RegEx(
        utf16le::CodePointIterator begin,
        utf16le::CodePointIterator end,
        boost::regex_constants::syntax_option_type flags,
        UINT32 lcid);

    boost::basic_regex<char32_t, WindowsChar32RegexTraits> const&
    GetRegex() const noexcept;

    // IUnknown

    HRESULT STDMETHODCALLTYPE
    QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept override;

    ULONG STDMETHODCALLTYPE
    AddRef() noexcept override;

    ULONG STDMETHODCALLTYPE
    Release() noexcept override;

    // IRegEx

    HRESULT STDMETHODCALLTYPE
    CreateMatchEnumerator(
        _In_ RegExString const* pInput,
        _In_ LONGLONG startOffset,
        RegExMatchFlags flags,
        _Outptr_ IRegExMatchEnumerator** ppEnumerator) noexcept override;
};
