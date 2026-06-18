#pragma once
#include <UnicodeRegEx.h>
#include <WindowsChar32RegexTraits.h>
#include "MatchEnumerator.h"
#include "OutputSink.h"

#include <TextEncoding.h>

class RegEx;

// Abstract base shared by RegExMatchResults (single-match) and RegExMatchEnumerator
// (iterates matches). Owns all per-input state and implements every IRegExMatchResults
// and IRegExMatchEnumerator vtable slot. Derives from IRegExMatchEnumerator so a leaf
// class only needs to add a QueryInterface override (and, for the single-match leaf,
// disable NextMatch). RegExMatchResults derives from the same base but its
// QueryInterface intentionally does not hand out IRegExMatchEnumerator.
class RegExMatchBase : public IRegExMatchEnumerator
{
    using VariantEnumerator = std::variant<
#ifdef TEXTENCODING_ENABLE_LATIN1
        MatchEnumerator<Latin1>,
#endif
        MatchEnumerator<Utf8>,
        MatchEnumerator<Utf16LE>,
        MatchEnumerator<Utf16BE>,
        MatchEnumerator<Sbcs>>;

    volatile long m_refCount = 1;
    wil::com_ptr<RegEx> const m_regex;
    void const* const m_inputData;
    size_t const m_inputSize;
    TextEncoding const m_inputEncoding;
    RegExEnumerationState m_state;
    VariantEnumerator m_variantEnumerator;
    OutputSink m_outputSink;
    std::u32string m_formatTemplate;
    boost::regex_constants::match_flag_type m_formatFlags;

protected:

    virtual ~RegExMatchBase();

    RegExMatchBase(
        _In_ RegEx* regex,
        RegExBytes const& input,
        TextEncoding inputEncoding,
        UINT_PTR startByteOffset,
        RegExMatchFlags matchFlags);

    // Runs regex_search (or regex_match if wholeStringMatch is true) from the initial
    // position. If a match is found, advances m_state to enumerating and returns true.
    // Otherwise leaves m_state at not_started and returns false. May throw.
    bool
    DoInitialSearch(bool wholeStringMatch);

public:

    // IUnknown (QueryInterface is overridden by each leaf class.)

    ULONG STDMETHODCALLTYPE
    AddRef() noexcept override;

    ULONG STDMETHODCALLTYPE
    Release() noexcept override;

    // IRegExMatchResults

    HRESULT STDMETHODCALLTYPE
    get_Input(_Out_ RegExBytes* pInput) noexcept override;

    HRESULT STDMETHODCALLTYPE
    get_InputCodePage(_Out_ UINT32* pCodePage) noexcept override;

    HRESULT STDMETHODCALLTYPE
    get_SubMatchCount(_Out_ UINT32* pCount) noexcept override;

    HRESULT STDMETHODCALLTYPE
    GetSubMatch(UINT32 subMatchIndex, _Out_ RegExSubMatch* pSubMatch) noexcept override;

    HRESULT STDMETHODCALLTYPE
    SetFormatTemplate(BSTR formatTemplate, RegExFormatFlags formatFlags) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Format(_Out_ BSTR* pOutputString) noexcept override;

    HRESULT STDMETHODCALLTYPE
    FormatTo(
        _In_ ISequentialStream* outputStream,
        UINT32 outputCodePage) noexcept override;

    HRESULT STDMETHODCALLTYPE
    CopyInput(
        LONGLONG inputOffset,
        LONGLONG size,
        _Out_ BSTR* pOutputString) noexcept override;

    HRESULT STDMETHODCALLTYPE
    CopyInputTo(
        LONGLONG inputOffset,
        LONGLONG size,
        _In_ ISequentialStream* outputStream,
        UINT32 outputCodePage) noexcept override;

    // IRegExMatchEnumerator

    HRESULT STDMETHODCALLTYPE
    get_State(_Out_ RegExEnumerationState* pState) noexcept override;

    // Default implementation advances the enumeration; overridden by RegExMatchResults
    // to return E_NOTIMPL.
    HRESULT STDMETHODCALLTYPE
    NextMatch(_Out_ VARIANT_BOOL* pFound) noexcept override;

private:

    static VariantEnumerator
    SelectEnumerator(
        TextEncoding inputEncoding,
        RegEx const& regex,
        RegExMatchFlags flags,
        void const* inputData,
        size_t inputSize,
        UINT_PTR startByteOffset);
};
