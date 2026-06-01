#include "pch.h"
#include "RegExMatchEnumerator.h"
#include "RegEx.h"

#include <utf.h>

static constexpr RegExString
MakeString(_In_reads_bytes_(size) void const* data, UINT_PTR size, RegExEncoding encoding) noexcept
{
    return {
        .data_ptr = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(data)),
        .size = static_cast<LONGLONG>(size),
        .encoding = encoding,
    };
}

template<class CharT>
static constexpr bool
StringIsAligned(RegExString const& str) noexcept
{
    return 0 == ((str.data_ptr | str.size) & (sizeof(CharT) - 1));
}

RegExMatchEnumerator::~RegExMatchEnumerator() = default;

RegExMatchEnumerator::RegExMatchEnumerator(
    _In_ RegEx* regex,
    _In_ RegExString const* pInput,
    RegExMatchFlags flags)
    : m_refCount(1)
    , m_matchFlags(static_cast<boost::regex_constants::match_flag_type>(flags))
    , m_regex(regex)
    , m_inputData(reinterpret_cast<void const*>(static_cast<UINT_PTR>(pInput->data_ptr)))
    , m_inputSize(static_cast<UINT_PTR>(pInput->size))
    , m_inputEncoding(pInput->encoding)
    , m_state(RegExEnumerationState_not_started)
    , m_variantIterator()
    , m_formatTemplate()
    , m_formatFlags(boost::regex_constants::format_default)
    , m_outputBuffer()
{
    // Validate the encoding parameter.
    // Initialize m_variantIterator with the correct type of iterator.
    // Iterator is constructed as empty since initial state needs to be not_started.
    // Iterator will be re-constructed with actual data on the first call to NextMatch().
    switch (m_inputEncoding)
    {
    case RegExEncoding_latin1:

        m_variantIterator.emplace<RegexIteratorLatin1>();
        break;

    case RegExEncoding_utf8:

        m_variantIterator.emplace<RegexIteratorUtf8>();
        break;

    case RegExEncoding_utf16le:

        if (!StringIsAligned<char16_t>(*pInput))
        {
            THROW_HR(E_INVALIDARG);
        }

        m_variantIterator.emplace<RegexIteratorUtf16LE>();
        break;

    case RegExEncoding_utf16be:

        if (!StringIsAligned<char16_t>(*pInput))
        {
            THROW_HR(E_INVALIDARG);
        }

        m_variantIterator.emplace<RegexIteratorUtf16BE>();
        break;

    default:

        THROW_HR(E_INVALIDARG);
    }
}

HRESULT __stdcall
RegExMatchEnumerator::QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept
{
    if (ppvObject == nullptr)
    {
        return E_POINTER;
    }

    if (riid == __uuidof(IUnknown) || riid == __uuidof(IRegExMatchEnumerator))
    {
        *ppvObject = static_cast<IRegExMatchEnumerator*>(this);
        AddRef();
        return S_OK;
    }

    *ppvObject = nullptr;
    return E_NOINTERFACE;
}

ULONG __stdcall
RegExMatchEnumerator::AddRef() noexcept
{
    return InterlockedIncrementNoFence(&m_refCount);
}

ULONG __stdcall
RegExMatchEnumerator::Release() noexcept
{
    ULONG ref = InterlockedDecrementRelease(&m_refCount);
    if (ref == 0)
    {
        delete this;
    }

    return ref;
}

HRESULT
RegExMatchEnumerator::GetInput(_Out_ RegExString* pInput) noexcept
{
    *pInput = MakeString(m_inputData, m_inputSize, m_inputEncoding);
    return S_OK;
}

HRESULT
RegExMatchEnumerator::GetState(_Out_ RegExEnumerationState* pState) noexcept
{
    *pState = m_state;
    return S_OK;
}

HRESULT
RegExMatchEnumerator::NextMatch(_Out_ VARIANT_BOOL* pFound) noexcept
{
    HRESULT hr;
    bool found = false;

    if (m_state == RegExEnumerationState_finished)
    {
        hr = E_NOT_VALID_STATE;
    }
    else
    {
        try
        {
            hr = std::visit([this](auto& iterator)
                { return VisitNextMatch(iterator); },
                m_variantIterator);
            found = m_state == RegExEnumerationState_enumerating;
        }
        catch (...)
        {
            hr = wil::ResultFromCaughtException();
        }
    }

    *pFound = found ? VARIANT_TRUE : VARIANT_FALSE;
    return hr;
}

HRESULT
RegExMatchEnumerator::GetSubMatchCount(_Out_ UINT32* pCount) noexcept
{
    if (m_state != RegExEnumerationState_enumerating)
    {
        *pCount = 0;
    }
    else
    {
        *pCount = std::visit([this](auto& iterator)
            { return VisitGetSubMatchCount(iterator); },
            m_variantIterator);
    }

    return S_OK;
}

HRESULT
RegExMatchEnumerator::GetSubMatch(UINT32 subMatchIndex, _Out_ RegExSubMatch* pSubMatch) noexcept
{
    HRESULT hr;
    *pSubMatch = {};

    if (m_state != RegExEnumerationState_enumerating)
    {
        // GetSubMatchCount() is 0, so any index is out of range.
        hr = E_INVALIDARG;
    }
    else
    {
        hr = std::visit([this, subMatchIndex, pSubMatch](auto& iterator)
            { return VisitGetSubMatch(iterator, subMatchIndex, pSubMatch); },
            m_variantIterator);
    }

    return hr;
}

HRESULT
RegExMatchEnumerator::GetSubMatchString(
    UINT32 subMatchIndex,
    RegExEncoding subMatchEncoding,
    _Out_ RegExString* pSubMatchString) noexcept
{
    HRESULT hr;
    *pSubMatchString = {};

    if (m_state != RegExEnumerationState_enumerating)
    {
        // GetSubMatchCount() is 0, so any index is out of range.
        hr = E_INVALIDARG;
    }
    else
    {
        try
        {
            m_outputBuffer.clear();
            hr = std::visit([this, subMatchIndex](auto& iterator)
                { return VisitGetSubMatchString(iterator, subMatchIndex); },
                m_variantIterator);

            if (SUCCEEDED(hr))
            {
                if (hr != S_OK)
                {
                    // No match for the specified subMatchIndex, return with encoding == 0.
                    assert(hr == S_FALSE);
                    hr = S_OK;
                }
                else
                {
                    hr = TranscodeOutput(subMatchEncoding, pSubMatchString);
                }
            }
        }
        catch (...)
        {
            hr = wil::ResultFromCaughtException();
        }
    }

    return hr;
}

HRESULT
RegExMatchEnumerator::SetFormatTemplate(BSTR formatTemplate, RegExFormatFlags formatFlags) noexcept
{
    HRESULT hr;

    try
    {
        static_assert(sizeof(formatTemplate[0]) == sizeof(char16_t), "BSTR must be UTF-16");
        auto formatIterators = utf16le::CodePointIterator::FromSpan(std::span(
            reinterpret_cast<char16_t const*>(formatTemplate),
            SysStringLen(formatTemplate)));
        m_formatTemplate.assign(formatIterators.first, formatIterators.second);
        m_formatFlags = static_cast<boost::regex_constants::match_flag_type>(formatFlags);
        hr = S_OK;
    }
    catch (...)
    {
        hr = wil::ResultFromCaughtException();
    }

    return hr;
}

HRESULT
RegExMatchEnumerator::Format(
    RegExEncoding outputEncoding,
    _Out_ RegExString* pOutput) noexcept
{
    HRESULT hr;
    *pOutput = {};

    if (m_state != RegExEnumerationState_enumerating)
    {
        hr = E_NOT_VALID_STATE;
    }
    else
    {
        try
        {
            m_outputBuffer.clear();
            hr = std::visit([this](auto& iterator)
                { return VisitFormat(iterator); },
                m_variantIterator);
            if (SUCCEEDED(hr))
            {
                hr = TranscodeOutput(outputEncoding, pOutput);
            }
        }
        catch (...)
        {
            hr = wil::ResultFromCaughtException();
        }
    }

    return hr;
}

HRESULT
RegExMatchEnumerator::VisitNextMatch(std::monostate) noexcept
{
    assert(false);
    return E_UNEXPECTED;
}

template<class IteratorT>
HRESULT
RegExMatchEnumerator::VisitNextMatch(
    boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>& iterator) noexcept(false)
{
    using RegexItT = boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>;

    bool atEnd;
    if (m_state != RegExEnumerationState_enumerating)
    {
        assert(m_state == RegExEnumerationState_not_started);
        using CharT = typename IteratorT::input_type;
        auto const [begin, end] = IteratorT::FromSpan(std::span(
            static_cast<CharT const*>(m_inputData),
            m_inputSize / sizeof(CharT)));
        auto const& newIterator =
            m_variantIterator.emplace<RegexItT>(begin, end, m_regex->GetRegex(), m_matchFlags);
        m_state = RegExEnumerationState_enumerating;

        // Note: iterator is no longer valid after emplace. Use newIterator instead.
        atEnd = newIterator == RegexItT();
    }
    else
    {
        ++iterator;
        atEnd = iterator == RegexItT();
    }

    if (atEnd)
    {
        m_state = RegExEnumerationState_finished;
    }

    return S_OK;
}

UINT32
RegExMatchEnumerator::VisitGetSubMatchCount(std::monostate) noexcept
{
    assert(false);
    return 0;
}

template<class IteratorT>
UINT32
RegExMatchEnumerator::VisitGetSubMatchCount(
    boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>& iterator) noexcept
{
    auto const& matchResults = *iterator;
    return static_cast<UINT32>(matchResults.size());
}

HRESULT
RegExMatchEnumerator::VisitGetSubMatch(std::monostate, UINT32, _Inout_ RegExSubMatch*) noexcept
{
    assert(false);
    return E_UNEXPECTED;
}

template<class IteratorT>
HRESULT
RegExMatchEnumerator::VisitGetSubMatch(
    boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>& iterator,
    UINT32 subMatchIndex,
    _Inout_ RegExSubMatch* pSubMatch) noexcept
{
    auto const& matchResults = *iterator;

    if (subMatchIndex >= matchResults.size())
    {
        *pSubMatch = {};
        return E_INVALIDARG;
    }

    auto const& submatch = matchResults[subMatchIndex];
    if (!submatch.matched)
    {
        pSubMatch->input_offset = 0;
        pSubMatch->size = 0;
        pSubMatch->matched = VARIANT_FALSE;
    }
    else
    {
        auto const firstOffset = submatch.first.ByteOffset(m_inputData);
        auto const secondOffset = submatch.second.ByteOffset(m_inputData);
        pSubMatch->input_offset = static_cast<LONGLONG>(firstOffset);
        pSubMatch->size = static_cast<LONGLONG>(secondOffset - firstOffset);
        pSubMatch->matched = VARIANT_TRUE;
    }

    return S_OK;
}

HRESULT
RegExMatchEnumerator::VisitGetSubMatchString(std::monostate, UINT32) noexcept
{
    assert(false);
    return E_UNEXPECTED;
}

template<class IteratorT>
HRESULT
RegExMatchEnumerator::VisitGetSubMatchString(
    boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>& iterator,
    UINT32 subMatchIndex) noexcept
{
    auto const& matchResults = *iterator;

    if (subMatchIndex >= matchResults.size())
    {
        return E_INVALIDARG;
    }

    auto const& submatch = matchResults[subMatchIndex];
    if (!submatch.matched)
    {
        return S_FALSE;
    }
    else
    {
        std::copy(submatch.first, submatch.second, std::back_inserter(m_outputBuffer));
        return S_OK;
    }
}

HRESULT
RegExMatchEnumerator::VisitFormat(std::monostate) noexcept
{
    assert(false);
    return E_UNEXPECTED;
}

template<class IteratorT>
HRESULT
RegExMatchEnumerator::VisitFormat(
    boost::regex_iterator<IteratorT, char32_t, WindowsChar32RegexTraits>& iterator) noexcept(false)
{
    iterator->format(
        std::back_inserter(m_outputBuffer),
        m_formatTemplate,
        m_formatFlags);
    return S_OK;
}

HRESULT
RegExMatchEnumerator::TranscodeOutput(
    RegExEncoding outputEncoding,
    _Out_ RegExString* pOutput) noexcept(false)
{
    void const* data;
    size_t size;

    switch (outputEncoding)
    {
    case RegExEncoding_latin1:
    {
        auto result = latin1::ConvertInPlace(m_outputBuffer);
        data = result.data();
        size = result.size_bytes();
        break;
    }
    case RegExEncoding_utf8:
    {
        auto result = utf8::ConvertInPlace(m_outputBuffer);
        data = result.data();
        size = result.size_bytes();
        break;
    }
    case RegExEncoding_utf16le:
    {
        auto result = utf16le::ConvertInPlace(m_outputBuffer);
        data = result.data();
        size = result.size_bytes();
        break;
    }
    case RegExEncoding_utf16be:
    {
        auto result = utf16be::ConvertInPlace(m_outputBuffer);
        data = result.data();
        size = result.size_bytes();
        break;
    }
    default:
        *pOutput = {};
        return E_INVALIDARG;
    }

    *pOutput = MakeString(data, size, outputEncoding);
    return S_OK;
}
