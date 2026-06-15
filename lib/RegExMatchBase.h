#pragma once
#include <UnicodeRegEx.h>
#include <utf.h>
#include <WindowsChar32RegexTraits.h>
#include "MatchEnumerator.h"
#include "OutputSink.h"

class RegEx;

// Abstract base shared by RegExMatchResults (single-match) and RegExMatchEnumerator
// (iterates matches). Owns all per-input state and implements every IRegExMatchResults
// and IRegExMatchEnumerator vtable slot. Derives from IRegExMatchEnumerator so a leaf
// class only needs to add a QueryInterface override (and, for the single-match leaf,
// disable NextMatch). RegExMatchResults derives from the same base but its
// QueryInterface intentionally does not hand out IRegExMatchEnumerator.
class RegExMatchBase : public IRegExMatchEnumerator
{
    using EnumeratorLatin1 = MatchEnumerator<latin1::CodePointIterator>;
    using EnumeratorUtf8 = MatchEnumerator<utf8::CodePointIterator>;
    using EnumeratorUtf16LE = MatchEnumerator<utf16le::CodePointIterator>;
    using EnumeratorUtf16BE = MatchEnumerator<utf16be::CodePointIterator>;

    using VariantEnumerator = std::variant<
        std::monostate,
        EnumeratorLatin1,
        EnumeratorUtf8,
        EnumeratorUtf16LE,
        EnumeratorUtf16BE>;

    volatile long m_refCount = 1;
    wil::com_ptr<RegEx> const m_regex;
    void const* const m_inputData;
    size_t const m_inputSize;
    RegExEncoding const m_inputEncoding;
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
        RegExEncoding inputEncoding,
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
    get_InputEncoding(_Out_ RegExEncoding* pEncoding) noexcept override;

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
        RegExEncoding outputEncoding) noexcept override;

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
        RegExEncoding outputEncoding) noexcept override;

    // IRegExMatchEnumerator

    HRESULT STDMETHODCALLTYPE
    get_State(_Out_ RegExEnumerationState* pState) noexcept override;

    // Default implementation advances the enumeration; overridden by RegExMatchResults
    // to return E_NOTIMPL.
    HRESULT STDMETHODCALLTYPE
    NextMatch(_Out_ VARIANT_BOOL* pFound) noexcept override;

private:

    // Returns E_UNEXPECTED.
    HRESULT
    VisitNextMatch(std::monostate) noexcept;

    // Expects not_started or enumerating.
    // Starts or continues the search. Updates m_state. Returns S_OK.
    template<class IteratorT>
    HRESULT
    VisitNextMatch(
        MatchEnumerator<IteratorT>& enumerator) noexcept(false);

    // Returns false.
    bool
    VisitInitialSearch(std::monostate, bool wholeStringMatch) noexcept;

    // Runs the initial regex_search or regex_match. Returns whether a match was found.
    template<class IteratorT>
    bool
    VisitInitialSearch(
        MatchEnumerator<IteratorT>& enumerator,
        bool wholeStringMatch) noexcept(false);

    // Returns 0.
    UINT32
    VisitGetSubMatchCount(std::monostate) noexcept;

    // Expects enumerating. Returns the number of submatches for the current match.
    template<class IteratorT>
    UINT32
    VisitGetSubMatchCount(
        MatchEnumerator<IteratorT>& enumerator) noexcept;

    // Returns E_UNEXPECTED.
    HRESULT
    VisitGetSubMatch(std::monostate, UINT32 subMatchIndex, _Inout_ RegExSubMatch* pSubMatch) noexcept;

    // Expects enumerating. Retrieves the specified submatch for the current match.
    // Returns S_OK or E_INVALIDARG.
    template<class IteratorT>
    HRESULT
    VisitGetSubMatch(
        MatchEnumerator<IteratorT>& enumerator,
        UINT32 subMatchIndex,
        _Inout_ RegExSubMatch* pSubMatch) noexcept;

    // Returns E_UNEXPECTED.
    HRESULT
    VisitFormat(std::monostate) noexcept;

    // Expects enumerating. Reads from m_formatTemplate and m_formatFlags, appends to m_outputBuffer.
    template<class IteratorT>
    HRESULT
    VisitFormat(
        MatchEnumerator<IteratorT>& enumerator) noexcept(false);
};
