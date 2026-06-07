#pragma once
#include <RepStrRegEx.h>

// Free-threaded library root object. Stateless; all methods are reentrant.
// Constructed via RepStrRegExLibraryCreate.
class RegExLibrary final : public IRegExLibrary
{
    volatile long m_refCount;
    wil::com_ptr<IUnknown> m_freeThreadedMarshaler;

public:

    RegExLibrary() noexcept;

    // IUnknown

    HRESULT STDMETHODCALLTYPE
    QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept override;

    ULONG STDMETHODCALLTYPE
    AddRef() noexcept override;

    ULONG STDMETHODCALLTYPE
    Release() noexcept override;

    // IRegExLibrary

    HRESULT STDMETHODCALLTYPE
    CreateRegEx(
        _In_ BSTR pattern,
        RegExSyntaxFlags syntaxFlags,
        UINT32 lcid,
        _Out_ RegExErrorCode* pErrorCode,
        _Outptr_ IRegEx** ppRegEx) noexcept override;

    HRESULT STDMETHODCALLTYPE
    EscapePatternLiteral(
        _In_ BSTR patternLiteral,
        RegExSyntaxFlags syntaxFlags,
        _Out_ BSTR* pEscapedPatternLiteral) noexcept override;

    HRESULT STDMETHODCALLTYPE
    EscapeFormatLiteral(
        _In_ BSTR formatLiteral,
        RegExFormatFlags formatFlags,
        _Out_ BSTR* pEscapedFormatLiteral) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Transcode(
        _In_ RegExBytes const* pInput,
        RegExEncoding inputEncoding,
        _Out_ BSTR* pOutput) noexcept override;

    HRESULT STDMETHODCALLTYPE
    TranscodeTo(
        _In_ RegExBytes const* pInput,
        RegExEncoding inputEncoding,
        RegExEncoding outputEncoding,
        _In_ ISequentialStream* outputStream) noexcept override;
};
