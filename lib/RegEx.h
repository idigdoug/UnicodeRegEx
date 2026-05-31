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

    HRESULT __stdcall
    QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept override;

    ULONG __stdcall
    AddRef() noexcept override;

    ULONG __stdcall
    Release() noexcept override;

    // IRegEx

    HRESULT
    CreateMatchEnumerator(
        _In_ RegExString const* pInput,
        RegExMatchFlags flags,
        _Outptr_ IRegExMatchEnumerator** ppEnumerator) noexcept override;
};
