#pragma once
#include <RepStrRegEx.h>
#include <utf.h>
#include <WindowsChar32RegexTraits.h>

class RegEx;

class RegExMatchEnumerator final : public IRegExMatchEnumerator
{
    template<class IteratorT>
    struct SearchState
    {
        IteratorT begin;
        IteratorT end;
        boost::match_results<IteratorT> matchResults;

        SearchState(_In_reads_bytes_(size) void const* data, size_t size);
    };

    using SearchStateLatin1 = SearchState<latin1::CodePointIterator>;
    using SearchStateUtf8 = SearchState<utf8::CodePointIterator>;
    using SearchStateUtf16LE = SearchState<utf16le::CodePointIterator>;
    using SearchStateUtf16BE = SearchState<utf16be::CodePointIterator>;

    using VariantSearchState = std::variant<
        std::monostate,
        SearchStateLatin1,
        SearchStateUtf8,
        SearchStateUtf16LE,
        SearchStateUtf16BE>;

    __volatile long m_refCount = 1;
    boost::regex_constants::match_flag_type m_matchFlags;
    wil::com_ptr<RegEx> const m_regex;
    void const* const m_inputData;
    size_t const m_inputSize;
    RegExEncoding const m_inputEncoding;
    RegExEnumerationState m_state;
    VariantSearchState m_variantSearchState;
    std::u32string m_formatTemplate;
    boost::regex_constants::match_flag_type m_formatFlags;
    std::u32string m_outputBuffer; // Sometimes contains char32_t, sometimes contains transcoded bytes.

public:

    ~RegExMatchEnumerator();

    RegExMatchEnumerator(
        _In_ RegEx* regex,
        _In_ RegExString const* pInput,
        RegExMatchFlags matchFlags);

    // IUnknown

    HRESULT STDMETHODCALLTYPE
    QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept override;

    ULONG STDMETHODCALLTYPE
    AddRef() noexcept override;

    ULONG STDMETHODCALLTYPE
    Release() noexcept override;

    // IRegExMatchEnumerator

    HRESULT STDMETHODCALLTYPE
    GetInput(_Out_ RegExString* pInput) noexcept override;

    HRESULT STDMETHODCALLTYPE
    GetState(_Out_ RegExEnumerationState* pState) noexcept override;

    HRESULT STDMETHODCALLTYPE
    NextMatch(_Out_ VARIANT_BOOL* pFound) noexcept override;

    HRESULT STDMETHODCALLTYPE
    GetSubMatchCount(_Out_ UINT32* pCount) noexcept override;

    HRESULT STDMETHODCALLTYPE
    GetSubMatch(UINT32 subMatchIndex, _Out_ RegExSubMatch* pSubMatch) noexcept override;

    HRESULT STDMETHODCALLTYPE
    GetSubMatchString(
        UINT32 subMatchIndex,
        RegExEncoding subMatchEncoding,
        _Out_ RegExString* pSubMatchString) noexcept override;

    HRESULT STDMETHODCALLTYPE
    SetFormatTemplate(BSTR formatTemplate, RegExFormatFlags formatFlags) noexcept override;

    HRESULT STDMETHODCALLTYPE
    Format(RegExEncoding outputEncoding, _Out_ RegExString* pOutputString) noexcept override;

private:

    // Returns E_UNEXPECTED.
    HRESULT
    VisitNextMatch(std::monostate) noexcept;

    // Expects not_started or enumerating.
    // Starts or continues the search. Updates m_state. Returns S_OK.
    template<class IteratorT>
    HRESULT
    VisitNextMatch(
        SearchState<IteratorT>& state) noexcept(false);

    // Returns 0.
    UINT32
    VisitGetSubMatchCount(std::monostate) noexcept;

    // Expects enumerating. Returns the number of submatches for the current match.
    template<class IteratorT>
    UINT32
    VisitGetSubMatchCount(
        SearchState<IteratorT>& state) noexcept;

    // Returns E_UNEXPECTED.
    HRESULT
    VisitGetSubMatch(std::monostate, UINT32 subMatchIndex, _Inout_ RegExSubMatch* pSubMatch) noexcept;

    // Expects enumerating. Retrieves the specified submatch for the current match. Returns S_OK or E_INVALIDARG.
    template<class IteratorT>
    HRESULT
    VisitGetSubMatch(
        SearchState<IteratorT>& state,
        UINT32 subMatchIndex,
        _Inout_ RegExSubMatch* pSubMatch) noexcept;

    // Returns E_UNEXPECTED.
    HRESULT
    VisitGetSubMatchString(std::monostate, UINT32 subMatchIndex) noexcept;

    // Expects enumerating. Appends the specified submatch to m_outputBuffer.
    // Returns S_OK (appended), S_FALSE (not matched), or E_INVALIDARG.
    template<class IteratorT>
    HRESULT
    VisitGetSubMatchString(
        SearchState<IteratorT>& state,
        UINT32 subMatchIndex) noexcept;

    // Returns E_UNEXPECTED.
    HRESULT
    VisitFormat(std::monostate) noexcept;

    // Expects enumerating. Reads from m_formatTemplate and m_formatFlags, appends to m_outputBuffer.
    template<class IteratorT>
    HRESULT
    VisitFormat(
        SearchState<IteratorT>& state) noexcept(false);

    // Converts m_outputBuffer to the specified encoding and returns it via pOutput.
    HRESULT
    TranscodeOutput(
        RegExEncoding outputEncoding,
        _Out_ RegExString* pOutput) noexcept(false);
};
