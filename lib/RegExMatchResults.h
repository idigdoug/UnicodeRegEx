#pragma once
#include "RegExMatchBase.h"

// Holds the results of a single regex search or match.
// Created by IRegEx::Search and IRegEx::Match (which return nullptr if no match was found).
class RegExMatchResults final : public RegExMatchBase
{
    RegExMatchResults(
        _In_ RegEx* regex,
        _In_ RegExBytes const* pInput,
        RegExEncoding inputEncoding,
        _In_ UINT_PTR startByteOffset,
        RegExMatchFlags matchFlags);

public:

    // Construct a RegExMatchResults by running regex_search/regex_match.
    // On success with a match found, returns S_OK with *ppResults != nullptr.
    // On success with no match, returns S_OK with *ppResults == nullptr.
    // On failure, returns the failure HRESULT with *ppResults == nullptr.
    static HRESULT
    Search(
        _In_ RegEx* regex,
        _In_ RegExBytes const* pInput,
        RegExEncoding inputEncoding,
        _In_ UINT_PTR startByteOffset,
        RegExMatchFlags matchFlags,
        bool wholeStringMatch,
        _Outptr_result_maybenull_ IRegExMatchResults** ppResults) noexcept;

    // IUnknown

    HRESULT STDMETHODCALLTYPE
    QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept override;

    // IRegExMatchEnumerator: a single-shot result is not an enumerator.
    HRESULT STDMETHODCALLTYPE
    NextMatch(_Out_ VARIANT_BOOL* pFound) noexcept override;
};
