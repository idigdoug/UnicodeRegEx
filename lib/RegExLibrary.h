#pragma once
#include <UnicodeRegEx.h>

// Free-threaded library root object. Stateless; all methods are reentrant.
// Constructed via UnicodeRegExLibraryCreate.
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
    GetEscapePatternLiteralChars(
        RegExSyntaxFlags syntaxFlags,
        _Out_ BSTR* pChars) noexcept override;

    HRESULT STDMETHODCALLTYPE
    GetEscapeFormatLiteralChars(
        RegExFormatFlags formatFlags,
        _Out_ BSTR* pChars) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Transcode(
        RegExBytes input,
        UINT32 inputCodePage,
        _Out_ BSTR* pOutput) noexcept override;

    HRESULT STDMETHODCALLTYPE
    TranscodeTo(
        RegExBytes input,
        UINT32 inputCodePage,
        _In_ ISequentialStream* outputStream,
        UINT32 outputCodePage) noexcept override;

    HRESULT STDMETHODCALLTYPE
    CreateMemoryStream(
        LONGLONG initialCapacity,
        _Outptr_ IRegExMemoryStream** ppStream) noexcept override;

    HRESULT STDMETHODCALLTYPE
    CreateFileStream(
        _In_ BSTR path,
        RegExFileStreamFlags flags,
        _Outptr_ IRegExFileStream** ppStream) noexcept override;

    HRESULT STDMETHODCALLTYPE
    CreateReplacementFileStream(
        _In_ BSTR finalPath,
        _Outptr_ IRegExFileStream** ppStream) noexcept override;

    HRESULT STDMETHODCALLTYPE
    CodePageIsSupported(
        UINT32 codePage,
        _Out_ VARIANT_BOOL* pSupported) noexcept override;

    HRESULT STDMETHODCALLTYPE
    MatchFlagsAreValid(
        RegExMatchFlags flags,
        _Out_ VARIANT_BOOL* pValid) noexcept override;

    HRESULT STDMETHODCALLTYPE
    FormatFlagsAreValid(
        RegExFormatFlags flags,
        _Out_ VARIANT_BOOL* pValid) noexcept override;
};
