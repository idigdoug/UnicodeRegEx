#pragma once
#include <UnicodeRegEx.h>
#include <WindowsChar32RegexTraits.h>
#include <TextEncoding.h>

class OutputSink;

class RegEx final : public IRegEx
{
    volatile long m_refCount;
    boost::basic_regex<char32_t, WindowsChar32RegexTraits> m_regex;
    wil::com_ptr<IUnknown> m_freeThreadedMarshaler;

public:

    ~RegEx();

    RegEx(
        Utf16LE::CodePointIterator begin,
        Utf16LE::CodePointIterator end,
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
        RegExBytes input,
        UINT32 inputCodePage,
        LONGLONG startOffset,
        RegExMatchFlags flags,
        _Outptr_result_maybenull_ IRegExMatchResults** ppResults) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Search(
        RegExBytes input,
        UINT32 inputCodePage,
        LONGLONG startOffset,
        RegExMatchFlags flags,
        _Outptr_result_maybenull_ IRegExMatchResults** ppResults) noexcept override;

    HRESULT STDMETHODCALLTYPE
    EnumerateMatches(
        RegExBytes input,
        UINT32 inputCodePage,
        LONGLONG startOffset,
        RegExMatchFlags flags,
        _Outptr_ IRegExMatchEnumerator** ppEnumerator) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Replace(
        RegExBytes input,
        UINT32 inputCodePage,
        LONGLONG startByteOffset,
        RegExMatchFlags matchFlags,
        _In_ BSTR formatTemplate,
        RegExFormatFlags formatFlags,
        _Out_ BSTR* pOutputString) noexcept override;

    HRESULT STDMETHODCALLTYPE
    ReplaceTo(
        RegExBytes input,
        UINT32 inputCodePage,
        LONGLONG startByteOffset,
        RegExMatchFlags matchFlags,
        _In_ BSTR formatTemplate,
        RegExFormatFlags formatFlags,
        _In_ ISequentialStream* outputStream,
        UINT32 outputCodePage) noexcept override;

private:

    HRESULT
    ReplaceImpl(
        RegExBytes const& input,
        UINT32 inputCodePage,
        LONGLONG startByteOffset,
        _In_ BSTR formatTemplate,
        boost::regex_constants::match_flag_type flags,
        OutputSink& outputSink) const;

    HRESULT
    SearchImpl(
        RegExBytes const& input,
        UINT32 inputCodePage,
        LONGLONG startByteOffset,
        RegExMatchFlags flags,
        bool wholeStringMatch,
        _Outptr_result_maybenull_ IRegExMatchResults** ppResults) noexcept;
};
