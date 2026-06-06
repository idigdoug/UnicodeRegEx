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
    get_Pattern(_Out_ BSTR* pValue) noexcept override;

    HRESULT STDMETHODCALLTYPE
    get_Flags(_Out_ RegExSyntaxFlags* pValue) noexcept override;

    HRESULT STDMETHODCALLTYPE
    get_Lcid(_Out_ UINT32* pValue) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Match(
        _In_ RegExBytes const* pInput,
        _In_ RegExEncoding inputEncoding,
        _In_ LONGLONG startOffset,
        RegExMatchFlags flags,
        _Outptr_opt_result_maybenull_ IRegExMatchResults** ppResults) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Search(
        _In_ RegExBytes const* pInput,
        _In_ RegExEncoding inputEncoding,
        _In_ LONGLONG startOffset,
        RegExMatchFlags flags,
        _Outptr_opt_result_maybenull_ IRegExMatchResults** ppResults) noexcept override;

    HRESULT STDMETHODCALLTYPE
    EnumerateMatches(
        _In_ RegExBytes const* pInput,
        _In_ RegExEncoding inputEncoding,
        _In_ LONGLONG startOffset,
        RegExMatchFlags flags,
        _Outptr_ IRegExMatchEnumerator** ppEnumerator) noexcept override;

private:

    HRESULT
    Search(
        _In_ RegExBytes const* pInput,
        _In_ RegExEncoding inputEncoding,
        _In_ LONGLONG startOffset,
        RegExMatchFlags flags,
        bool wholeStringMatch,
        _Outptr_opt_result_maybenull_ IRegExMatchResults** ppResults) noexcept;
};
