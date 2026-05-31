#pragma once
#include <RepStrRegEx.h>
#include <utf.h>
#include <WindowsChar32RegexTraits.h>

class RegEx;

class RegExMatchEnumerator final : public IRegExMatchEnumerator
{
    using VariantIterator = std::variant<
        std::monostate,
        boost::regex_iterator<latin1::CodePointIterator, char32_t, WindowsChar32RegexTraits>,
        boost::regex_iterator<utf8::CodePointIterator, char32_t, WindowsChar32RegexTraits>,
        boost::regex_iterator<utf16le::CodePointIterator, char32_t, WindowsChar32RegexTraits>,
        boost::regex_iterator<utf16be::CodePointIterator, char32_t, WindowsChar32RegexTraits>>;

    __volatile long m_refCount = 1;
    boost::regex_constants::match_flag_type m_matchFlags;
    wil::com_ptr<RegEx> m_regex;
    void const* m_inputData;
    size_t m_inputSize;
    RegExEnumerationState m_state;
    boost::regex_constants::match_flag_type m_formatFlags;
    VariantIterator m_variantIterator; // m_variantIterator.index() is the input's encoding.
    std::u32string m_formatTemplate;
    std::u32string m_outputBuffer;

public:

    ~RegExMatchEnumerator();

    RegExMatchEnumerator(
        _In_ RegEx* regex,
        _In_ RegExString const* pInput,
        RegExMatchFlags matchFlags);

    // IUnknown

    HRESULT __stdcall
    QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept override;

    ULONG __stdcall
    AddRef() noexcept override;

    ULONG __stdcall
    Release() noexcept override;

    // IRegExMatchEnumerator

    HRESULT
    GetInput(_Out_ RegExString* pInput) noexcept override;

    HRESULT
    GetState(_Out_ RegExEnumerationState* pState) noexcept override;

    HRESULT
    NextMatch(_Out_ VARIANT_BOOL* pFound) noexcept override;

    HRESULT
    GetSubMatchCount(_Out_ UINT32* pCount) noexcept override;

    HRESULT
    GetSubMatch(UINT32 subMatchIndex, _Out_ RegExSubMatch* pSubMatch) noexcept override;

    HRESULT
    GetSubMatchString(
        UINT32 subMatchIndex,
        RegExEncoding subMatchEncoding,
        _Out_ RegExString* pSubMatchString) noexcept override;

    HRESULT
    SetFormatTemplate(BSTR formatTemplate, RegExFormatFlags formatFlags) noexcept override;

    HRESULT
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
        boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>& iterator) noexcept(false);

    // Returns 0.
    UINT32
    VisitGetSubMatchCount(std::monostate) noexcept;

    // Expects enumerating. Returns the number of submatches for the current match.
    template<class IteratorT>
    UINT32
    VisitGetSubMatchCount(
        boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>& iterator) noexcept;
    
    // Returns E_UNEXPECTED.
    HRESULT
    VisitGetSubMatch(std::monostate, UINT32 subMatchIndex, _Inout_ RegExSubMatch* pSubMatch) noexcept;

    // Expects enumerating. Retrieves the specified submatch for the current match. Returns S_OK or E_INVALIDARG.
    template<class IteratorT>
    HRESULT
    VisitGetSubMatch(
        boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>& iterator,
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
        boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>& iterator,
        UINT32 subMatchIndex) noexcept;

    // Returns E_UNEXPECTED.
    HRESULT
    VisitFormat(std::monostate) noexcept;

    // Expects enumerating. Reads from m_formatTemplate and m_formatFlags, appends to m_outputBuffer.
    template<class IteratorT>
    HRESULT
    VisitFormat(
        boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>& iterator) noexcept(false);

    // Converts m_outputBuffer to the specified encoding and returns it via pOutput.
    HRESULT
    TranscodeOutput(
        RegExEncoding outputEncoding,
        _Out_ RegExString* pOutput) noexcept(false);
};
