#pragma once
#include <RepStrRegEx.h>

template<class CharT>
constexpr RegExBytes
MakeString(std::basic_string_view<CharT> sv)
{
    return {
        .data_ptr = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(sv.data())),
        .size = static_cast<LONGLONG>(sv.size() * sizeof(sv[0])),
    };
}

template<class CharT>
constexpr std::basic_string_view<CharT>
MakeView(RegExBytes const& str) noexcept
{
    return std::basic_string_view<CharT>(
        reinterpret_cast<CharT const*>(str.data_ptr),
        static_cast<UINT_PTR>(str.size / sizeof(CharT)));
}

inline std::wstring_view
MakeView(BSTR pStr) noexcept
{
    static_assert(sizeof(pStr[0]) == sizeof(wchar_t), "BSTR must be UTF-16");
    return std::wstring_view(reinterpret_cast<wchar_t const*>(pStr), SysStringLen(pStr));
}

// Byte-swap a UTF-16 buffer in place (e.g. to produce UTF-16BE bytes from a u"..."
// literal that is naturally UTF-16LE on Windows). Returns a view of the swapped data.
inline std::u16string_view
ByteSwap16(std::span<char16_t> chars) noexcept
{
    for (auto& ch : chars)
    {
        ch = _byteswap_ushort(ch);
    }
    return std::u16string_view(chars.data(), chars.size());
}

// Minimal in-memory ISequentialStream implementation for tests.
// Write appends to an internal buffer; Read is not supported.
class TestMemoryStream final : public ISequentialStream
{
    std::vector<BYTE> m_buffer;
    volatile long m_refCount = 1;

public:

    // Returns the bytes written so far as a string_view of the requested character type.
    template<class CharT = char>
    std::basic_string_view<CharT>
    View() const noexcept
    {
        return std::basic_string_view<CharT>(
            reinterpret_cast<CharT const*>(m_buffer.data()),
            m_buffer.size() / sizeof(CharT));
    }

    // Returns a pointer + size view of the bytes written so far.
    std::span<BYTE const>
    Bytes() const noexcept
    {
        return std::span<BYTE const>(m_buffer.data(), m_buffer.size());
    }

    // IUnknown
    HRESULT STDMETHODCALLTYPE
    QueryInterface(REFIID riid, _Outptr_ void** ppvObject) noexcept override
    {
        if (ppvObject == nullptr)
        {
            return E_POINTER;
        }

        if (riid == __uuidof(IUnknown) || riid == __uuidof(ISequentialStream))
        {
            *ppvObject = static_cast<ISequentialStream*>(this);
            AddRef();
            return S_OK;
        }

        *ppvObject = nullptr;
        return E_NOINTERFACE;
    }

    ULONG STDMETHODCALLTYPE
    AddRef() noexcept override
    {
        return InterlockedIncrementNoFence(&m_refCount);
    }

    ULONG STDMETHODCALLTYPE
    Release() noexcept override
    {
        auto const ref = InterlockedDecrementRelease(&m_refCount);
        if (ref == 0)
        {
            delete this;
        }
        return ref;
    }

    // ISequentialStream
    HRESULT STDMETHODCALLTYPE
    Read(void*, ULONG, ULONG*) noexcept override
    {
        return E_NOTIMPL;
    }

    HRESULT STDMETHODCALLTYPE
    Write(void const* pv, ULONG cb, ULONG* pcbWritten) noexcept override
    {
        try
        {
            auto const* p = static_cast<BYTE const*>(pv);
            m_buffer.insert(m_buffer.end(), p, p + cb);
            if (pcbWritten != nullptr)
            {
                *pcbWritten = cb;
            }
            return S_OK;
        }
        catch (...)
        {
            return E_OUTOFMEMORY;
        }
    }
};

// Returns a process-wide IRegExLibrary instance, lazily creating it on first use.
inline IRegExLibrary*
GetLibrary()
{
    static wil::com_ptr<IRegExLibrary> s_library;
    if (!s_library)
    {
        wil::com_ptr<IRegExLibrary> library;
        if (SUCCEEDED(RepStrRegExLibraryCreate(library.put())))
        {
            s_library = std::move(library);
        }
    }
    return s_library.get();
}

// Compiles a regex and returns the HRESULT and error code. Use for tests that
// exercise failure paths or want to inspect the RegExErrorCode.
inline HRESULT
TryMakeRegEx(
    std::wstring_view pattern,
    RegExSyntaxFlags syntaxFlags,
    UINT32 lcid,
    _Out_ RegExErrorCode* pErrorCode,
    _Out_ wil::com_ptr<IRegEx>& regex) noexcept
{
    wil::unique_bstr patternBstr(SysAllocStringLen(pattern.data(), static_cast<UINT>(pattern.size())));
    regex.reset();
    return GetLibrary()->CreateRegEx(patternBstr.get(), syntaxFlags, lcid, pErrorCode, regex.put());
}

// Compiles a regex. Asserts success and returns a ready-to-use IRegEx.
// Use for the common case where a test expects the pattern to compile.
inline wil::com_ptr<IRegEx>
MakeRegEx(
    std::wstring_view pattern,
    RegExSyntaxFlags syntaxFlags = RegExSyntaxFlags_ECMAScript,
    UINT32 lcid = 0)
{
    RegExErrorCode errorCode;
    wil::com_ptr<IRegEx> regex;
    HRESULT hr = TryMakeRegEx(pattern, syntaxFlags, lcid, &errorCode, regex);
    Microsoft::VisualStudio::CppUnitTestFramework::Assert::AreEqual(S_OK, hr, L"MakeRegEx: CreateRegEx failed");
    Microsoft::VisualStudio::CppUnitTestFramework::Assert::IsNotNull(regex.get(), L"MakeRegEx: regex is null");
    return regex;
}
