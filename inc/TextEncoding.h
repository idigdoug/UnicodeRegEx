#pragma once
#include <span>
#include <optional>
#include <variant>
#include <assert.h>

#ifndef TEXTENCODING_FORCEINLINE
#ifdef NDEBUG
#define TEXTENCODING_FORCEINLINE __forceinline
#else // NDEBUG
#define TEXTENCODING_FORCEINLINE
#endif // NDEBUG
#endif // TEXTENCODING_FORCEINLINE

// Utilities for 32-bit code points.
namespace CodePoint
{
    constexpr char32_t ReplacementChar = 0xFFFD;

    // codePoint <= 0x7F
    constexpr bool
    IsAscii(char32_t codePoint) noexcept
    {
        return codePoint <= 0x7F;
    }

    // codePoint <= 0xFF
    constexpr bool
    IsLatin1(char32_t codePoint) noexcept
    {
        return codePoint <= 0xFF;
    }

    // codePoint <= 0xFFFF
    constexpr bool
    IsBmp(char32_t codePoint) noexcept
    {
        return codePoint <= 0xFFFF;
    }

    constexpr bool
    IsScalarBmp(char32_t codePoint) noexcept
    {
        return codePoint < 0xD800 || (codePoint <= 0xFFFF && codePoint > 0xDFFF);
    }

    // codePoint <= 0x10FFFF
    constexpr bool
    IsCodePoint(char32_t codePoint) noexcept
    {
        return codePoint <= 0x10FFFF;
    }

    // codePoint >= 0xD800 && codePoint <= 0xDC00
    constexpr bool
    IsHighSurrogate(char32_t ch) noexcept
    {
        return (ch & 0xFFFFFC00) == 0xD800;
    }

    // codePoint >= 0xDC00 && codePoint <= 0xDFFF
    constexpr bool
    IsLowSurrogate(char32_t ch) noexcept
    {
        return (ch & 0xFFFFFC00) == 0xDC00;
    }

    // Convert a surrogate pair to a supplementary-plane code point.
    constexpr char32_t
    FromSurrogatePair(char16_t high, char16_t low) noexcept
    {
        assert(0xD800 <= high && high <= 0xDBFF);
        assert(0xDC00 <= low && low <= 0xDFFF);
        return ((static_cast<char32_t>(high) - 0xD800) << 10) + (low - 0xDC00) + 0x10000;
    }
} // namespace CodePoint

namespace _textEncodingDetail
{
    template<typename IteratorT>
    struct CodePointRange
    {
        IteratorT begin;
        IteratorT end;
    };

    template<typename IteratorT>
    struct CodePointRangeAndPos
    {
        IteratorT pos; // May be IteratorT() if the specified position was invalid.
        IteratorT begin;
        IteratorT end;
    };

    template<typename EncodingT>
    class CodePointIterator
        : private EncodingT::_codePointIterator
    {
        friend EncodingT;
        using EncodedCharT = typename EncodingT::encoded_char;

        constexpr
        CodePointIterator(EncodedCharT const* pos, EncodedCharT const* begin, EncodedCharT const* end) noexcept
            : EncodingT::_codePointIterator(pos, begin, end)
        {
            assert(begin <= pos);
            assert(pos <= end);
        }

        // Returns a begin/end pair of CodePointIterators for the given char span.
        static constexpr CodePointRange<CodePointIterator>
        MakeCodePointRange(std::span<EncodedCharT const> chars) noexcept
        {
            auto const pBegin = chars.data();
            auto const pEnd = chars.data() + chars.size();
            return { CodePointIterator(pBegin, pBegin, pEnd), CodePointIterator(pEnd, pBegin, pEnd) };
        }

        // Returns begin and end like FromSpan.
        // Also returns a position iterator for begin + byteOffset, or CodePointIterator() if the
        // byteOffset is past the end of the span or if it points at an invalid position in the
        // input byte sequence (e.g. middle of a char16_t, middle of a UTF-8 sequence, etc.).
        static constexpr CodePointRangeAndPos<CodePointIterator>
        MakeCodePointRangeAndPos(std::span<EncodedCharT const> chars, size_t byteOffset) noexcept
        {
            auto const pBegin = chars.data();
            auto const pEnd = chars.data() + chars.size();
            if (byteOffset <= chars.size_bytes() && byteOffset % sizeof(EncodedCharT) == 0)
            {
                auto const pPos = chars.data() + byteOffset / sizeof(EncodedCharT);
                auto const pos = CodePointIterator(pPos, pBegin, pEnd);

                // Verify that we're not on an invalid position. If we're on a valid position (other
                // than begin or end) then going backwards one position and then forwards one position
                // should will always yield the starting position (for UTF-8 or UTF-16).
                if (auto pos2 = pos; pPos == pBegin || pPos == pEnd || ++(--(pos2)) == pos)
                {
                    return { pos, CodePointIterator(pBegin, pBegin, pEnd), CodePointIterator(pEnd, pBegin, pEnd) };
                }
            }

            return { CodePointIterator(), CodePointIterator(pBegin, pBegin, pEnd), CodePointIterator(pEnd, pBegin, pEnd) };
        }

    public:

        using iterator_category = std::bidirectional_iterator_tag;
        using value_type = char32_t;
        using difference_type = std::ptrdiff_t;
        using pointer = char32_t const*;
        using reference = char32_t;
        using encoded_char = EncodedCharT;

        constexpr
        CodePointIterator() noexcept
            : EncodingT::_codePointIterator(nullptr, nullptr, nullptr) {}

        // Returns the number of bytes between begin and the current iterator position.
        // The begin value should be chars.data() from the chars value passed to FromSpan.
        constexpr size_t
        ByteOffset(void const* begin) const noexcept
        {
            assert(this->m_begin == static_cast<encoded_char const*>(begin));
            assert(this->m_pos >= static_cast<encoded_char const*>(begin));
            return (this->m_pos - static_cast<encoded_char const*>(begin)) * sizeof(encoded_char);
        }

        TEXTENCODING_FORCEINLINE
        constexpr value_type
        operator*() const noexcept
        {
            assert(this->m_begin <= this->m_pos);
            assert(this->m_pos < this->m_end);
            return this->Read();
        }

        TEXTENCODING_FORCEINLINE
        constexpr CodePointIterator&
        operator++() noexcept
        {
            assert(this->m_begin <= this->m_pos);
            assert(this->m_pos < this->m_end);
            this->Increment();
            return *this;
        }

        TEXTENCODING_FORCEINLINE
        constexpr CodePointIterator&
        operator--() noexcept
        {
            assert(this->m_begin < this->m_pos);
            assert(this->m_pos <= this->m_end);
            this->Decrement();
            return *this;
        }

        constexpr CodePointIterator
        operator++(int) noexcept
        {
            assert(this->m_begin <= this->m_pos);
            assert(this->m_pos < this->m_end);
            auto tmp = *this;
            this->Increment();
            return tmp;
        }

        constexpr CodePointIterator
        operator--(int) noexcept
        {
            assert(this->m_begin < this->m_pos);
            assert(this->m_pos <= this->m_end);
            auto tmp = *this;
            this->Decrement();
            return tmp;
        }

        constexpr bool
        operator==(CodePointIterator const& other) const noexcept
        {
            return this->m_pos == other.m_pos;
        }

        constexpr bool
        operator!=(CodePointIterator const& other) const noexcept
        {
            return this->m_pos != other.m_pos;
        }
    };
} // namespace _textEncodingDetail

// Conversions between Latin1 (aka ucs1, ISO-8859-1, cp28591) and 32-bit code points.
struct Latin1
{
    using encoded_char = char;

    // A random-access iterator over char data that dereferences to char32_t (Latin-1 identity mapping).
    class CodePointIterator
    {
        friend struct Latin1;
        encoded_char const* m_pos;

        constexpr explicit
        CodePointIterator(encoded_char const* pos) noexcept
            : m_pos(pos)
        {}

    public:

        using iterator_category = std::random_access_iterator_tag;
        using value_type = char32_t;
        using difference_type = std::ptrdiff_t;
        using pointer = char32_t const*;
        using reference = char32_t;
        using encoded_char = encoded_char;

        constexpr
        CodePointIterator() noexcept
            : m_pos(nullptr)
        {}

        // Returns the number of bytes between begin and the current iterator position.
        // The begin value should be chars.data() from the chars value passed to FromSpan.
        constexpr size_t
        ByteOffset(void const* begin) const noexcept
        {
            assert(m_pos >= static_cast<encoded_char const*>(begin));
            return (m_pos - static_cast<encoded_char const*>(begin)) * sizeof(encoded_char);
        }

        constexpr value_type
        operator*() const noexcept
        {
            return static_cast<unsigned char>(m_pos[0]);
        }

        constexpr value_type
        operator[](difference_type n) const noexcept
        {
            return static_cast<unsigned char>(m_pos[n]);
        }

        constexpr CodePointIterator&
        operator++() noexcept
        {
            m_pos += 1;
            return *this;
        }

        constexpr CodePointIterator
        operator++(int) noexcept
        {
            auto tmp = *this;
            m_pos += 1;
            return tmp;
        }

        constexpr CodePointIterator&
        operator--() noexcept
        {
            m_pos -= 1;
            return *this;
        }

        constexpr CodePointIterator
        operator--(int) noexcept
        {
            auto tmp = *this;
            m_pos -= 1;
            return tmp;
        }

        constexpr CodePointIterator&
        operator+=(difference_type n) noexcept
        {
            m_pos += n;
            return *this;
        }

        constexpr CodePointIterator&
        operator-=(difference_type n) noexcept
        {
            m_pos -= n;
            return *this;
        }

        friend constexpr CodePointIterator
        operator+(CodePointIterator it, difference_type n) noexcept
        {
            return it += n;
        }

        friend constexpr CodePointIterator
        operator+(difference_type n, CodePointIterator it) noexcept
        {
            return it += n;
        }

        friend constexpr CodePointIterator
        operator-(CodePointIterator it, difference_type n) noexcept
        {
            return it -= n;
        }

        friend constexpr difference_type
        operator-(CodePointIterator const& a, CodePointIterator const& b) noexcept
        {
            return a.m_pos - b.m_pos;
        }

        constexpr bool
        operator==(CodePointIterator const& other) const noexcept
        {
            return m_pos == other.m_pos;
        }

        constexpr bool
        operator!=(CodePointIterator const& other) const noexcept
        {
            return m_pos != other.m_pos;
        }

        constexpr bool
        operator<(CodePointIterator const& other) const noexcept
        {
            return m_pos < other.m_pos;
        }

        constexpr bool
        operator>(CodePointIterator const& other) const noexcept
        {
            return m_pos > other.m_pos;
        }

        constexpr bool
        operator<=(CodePointIterator const& other) const noexcept
        {
            return m_pos <= other.m_pos;
        }

        constexpr bool
        operator>=(CodePointIterator const& other) const noexcept
        {
            return m_pos >= other.m_pos;
        }
    };

    using CodePointRange = _textEncodingDetail::CodePointRange<CodePointIterator>;
    using CodePointRangeAndPos = _textEncodingDetail::CodePointRangeAndPos<CodePointIterator>;

    constexpr bool
    operator==(Latin1 const&) const noexcept
    {
        return true;
    }

    static std::optional<Latin1>
    TryFromCodePage(unsigned codePage) noexcept
    {
        if (codePage == MyCodePage)
        {
            return Latin1();
        }
        return std::nullopt;
    }

    static constexpr unsigned
    CodePage() noexcept
    {
        return MyCodePage;
    }

    // Returns a begin/end pair of CodePointIterators for the given char span.
    constexpr CodePointRange
    MakeCodePointRange(std::span<encoded_char const> chars) const noexcept
    {
        auto const pBegin = chars.data();
        auto const pEnd = chars.data() + chars.size();
        return { CodePointIterator(pBegin), CodePointIterator(pEnd) };
    }

    // Returns begin and end like FromSpan.
    // Also returns a position iterator for begin + byteOffset, or CodePointIterator() if the
    // byteOffset is past the end of the span.
    constexpr CodePointRangeAndPos
    MakeCodePointRangeAndPos(std::span<encoded_char const> chars, size_t byteOffset) const noexcept
    {
        auto const pBegin = chars.data();
        auto const pEnd = chars.data() + chars.size();
        auto const pos = byteOffset <= chars.size_bytes() ? chars.data() + byteOffset : nullptr;
        return { CodePointIterator(pos), CodePointIterator(pBegin), CodePointIterator(pEnd) };
    }

    // Converts a sequence of code points into a Latin1 string.
    // Overwrites the input (always fits). Returns the Latin1 buffer.
    std::span<encoded_char>
    ConvertInPlace(std::span<char32_t> codePoints) const noexcept
    {
        auto* const dst = reinterpret_cast<encoded_char*>(codePoints.data());
        size_t dstPos = 0;
        for (auto codePoint : codePoints)
        {
            dstPos += Encode(dst + dstPos, codePoint);
        }

        return std::span{ dst, dstPos };
    }

    // Encodes a single code point into Latin1.
    // Returns the number of bytes written (always 1).
    // Encodes out-of-range code points (above 0xFF) as '?'.
    constexpr unsigned
    Encode(_Out_writes_all_(1) encoded_char* dst, char32_t codePoint) const noexcept
    {
        dst[0] = CodePoint::IsLatin1(codePoint) ? static_cast<encoded_char>(codePoint) : '?';
        return 1;
    }

private:

    static constexpr unsigned MyCodePage = 28591;
};

// Conversions between Windows SBCS (e.g. cp1252) and 32-bit code points.
class Sbcs
{
    struct Table
    {
        wchar_t Values[256];
        unsigned CodePage;
        char DefaultChar;
    };

    Table const* m_table;

    explicit
    Sbcs(_In_ Table const* table) noexcept
        : m_table(table)
    {}

public:

    using encoded_char = char;

    // A random-access iterator over char data that dereferences to char32_t (SBCS mapping).
    class CodePointIterator
    {
        friend class Sbcs;
        encoded_char const* m_pos;
        Table const* m_table;

        constexpr explicit
        CodePointIterator(encoded_char const* pos, Table const* m_table) noexcept
            : m_pos(pos)
            , m_table(m_table)
        {}

    public:

        using iterator_category = std::random_access_iterator_tag;
        using value_type = char32_t;
        using difference_type = std::ptrdiff_t;
        using pointer = char32_t const*;
        using reference = char32_t;
        using encoded_char = encoded_char;

        constexpr
        CodePointIterator() noexcept
            : m_pos(nullptr)
            , m_table(nullptr)
        {}

        // Returns the number of bytes between begin and the current iterator position.
        // The begin value should be chars.data() from the chars value passed to FromSpan.
        constexpr size_t
        ByteOffset(void const* begin) const noexcept
        {
            assert(m_pos >= static_cast<encoded_char const*>(begin));
            return (m_pos - static_cast<encoded_char const*>(begin)) * sizeof(encoded_char);
        }

        constexpr value_type
        operator*() const noexcept
        {
            return m_table->Values[static_cast<unsigned char>(m_pos[0])];
        }

        constexpr value_type
        operator[](difference_type n) const noexcept
        {
            return m_table->Values[static_cast<unsigned char>(m_pos[n])];
        }

        constexpr CodePointIterator&
        operator++() noexcept
        {
            m_pos += 1;
            return *this;
        }

        constexpr CodePointIterator
        operator++(int) noexcept
        {
            auto tmp = *this;
            m_pos += 1;
            return tmp;
        }

        constexpr CodePointIterator&
        operator--() noexcept
        {
            m_pos -= 1;
            return *this;
        }

        constexpr CodePointIterator
        operator--(int) noexcept
        {
            auto tmp = *this;
            m_pos -= 1;
            return tmp;
        }

        constexpr CodePointIterator&
        operator+=(difference_type n) noexcept
        {
            m_pos += n;
            return *this;
        }

        constexpr CodePointIterator&
        operator-=(difference_type n) noexcept
        {
            m_pos -= n;
            return *this;
        }

        friend constexpr CodePointIterator
        operator+(CodePointIterator it, difference_type n) noexcept
        {
            return it += n;
        }

        friend constexpr CodePointIterator
        operator+(difference_type n, CodePointIterator it) noexcept
        {
            return it += n;
        }

        friend constexpr CodePointIterator
        operator-(CodePointIterator it, difference_type n) noexcept
        {
            return it -= n;
        }

        friend constexpr difference_type
        operator-(CodePointIterator const& a, CodePointIterator const& b) noexcept
        {
            return a.m_pos - b.m_pos;
        }

        constexpr bool
        operator==(CodePointIterator const& other) const noexcept
        {
            return m_pos == other.m_pos;
        }

        constexpr bool
        operator!=(CodePointIterator const& other) const noexcept
        {
            return m_pos != other.m_pos;
        }

        constexpr bool
        operator<(CodePointIterator const& other) const noexcept
        {
            return m_pos < other.m_pos;
        }

        constexpr bool
        operator>(CodePointIterator const& other) const noexcept
        {
            return m_pos > other.m_pos;
        }

        constexpr bool
        operator<=(CodePointIterator const& other) const noexcept
        {
            return m_pos <= other.m_pos;
        }

        constexpr bool
        operator>=(CodePointIterator const& other) const noexcept
        {
            return m_pos >= other.m_pos;
        }
    };

    using CodePointRange = _textEncodingDetail::CodePointRange<CodePointIterator>;
    using CodePointRangeAndPos = _textEncodingDetail::CodePointRangeAndPos<CodePointIterator>;

    bool
    operator==(Sbcs const& other) const noexcept
    {
        return m_table == other.m_table;
    }

    static std::optional<Sbcs>
    TryFromCodePage(unsigned codePage) noexcept;        

    unsigned
    CodePage() const noexcept
    {
        return m_table->CodePage;
    }

    // Returns a begin/end pair of CodePointIterators for the given char span.
    CodePointRange
    MakeCodePointRange(std::span<encoded_char const> chars) const noexcept
    {
        auto const pBegin = chars.data();
        auto const pEnd = chars.data() + chars.size();
        return { CodePointIterator(pBegin, m_table), CodePointIterator(pEnd, m_table) };
    }

    // Returns begin and end like FromSpan.
    // Also returns a position iterator for begin + byteOffset, or CodePointIterator() if the
    // byteOffset is past the end of the span.
    CodePointRangeAndPos
    MakeCodePointRangeAndPos(std::span<encoded_char const> chars, size_t byteOffset) const noexcept
    {
        auto const pBegin = chars.data();
        auto const pEnd = chars.data() + chars.size();
        auto const pos = byteOffset <= chars.size_bytes() ? chars.data() + byteOffset : nullptr;
        return { CodePointIterator(pos, m_table), CodePointIterator(pBegin, m_table), CodePointIterator(pEnd, m_table) };
    }

    // Converts a sequence of code points into an SBCS string.
    // Overwrites the input (always fits). Returns the SBCS buffer.
    std::span<encoded_char>
    ConvertInPlace(std::span<char32_t> codePoints) const noexcept;

    // Encodes a single code point into the SBCS.
    // Returns the number of bytes written (always 1).
    // Encodes out-of-range code points (above 0xFF) as '?'.
    unsigned
    Encode(_Out_writes_all_(1) encoded_char* dst, char32_t codePoint) const noexcept
    {
        auto converted = ConvertInPlace({ &codePoint, 1 });
        *dst = converted[0];
        return 1;
    }
};

// Conversions between utf-8 (cp65001) and 32-bit code points.
struct Utf8
{
    using encoded_char = char8_t;

    // A bidirectional iterator over UTF-8 data that dereferences to char32_t.
    // Invalid sequences are returned as U+FFFD.
    using CodePointIterator = _textEncodingDetail::CodePointIterator<Utf8>;

    // A begin/end iterator pair, created by CodePointIterator::FromSpan.
    using CodePointRange = _textEncodingDetail::CodePointRange<CodePointIterator>;

    // A begin/end/pos iterator triple, created by CodePointIterator::FromSpanAndByteOffset.
    using CodePointRangeAndPos = _textEncodingDetail::CodePointRangeAndPos<CodePointIterator>;

    constexpr bool
    operator==(Utf8 const&) const noexcept
    {
        return true;
    }

    static std::optional<Utf8>
    TryFromCodePage(unsigned codePage) noexcept
    {
        if (codePage == MyCodePage)
        {
            return Utf8();
        }
        return std::nullopt;
    }

    static constexpr unsigned
    CodePage() noexcept
    {
        return MyCodePage;
    }

    // Returns a begin/end pair of CodePointIterators for the given char span.
    constexpr CodePointRange
    MakeCodePointRange(std::span<encoded_char const> chars) const noexcept
    {
        return CodePointIterator::MakeCodePointRange(chars);
    }

    // Returns begin and end like FromSpan.
    // Also returns a position iterator for begin + byteOffset, or CodePointIterator() if the
    // byteOffset is past the end of the span or if it points at an invalid position in the
    // input byte sequence (e.g. middle of a char16_t, middle of a UTF-8 sequence, etc.).
    constexpr CodePointRangeAndPos
    MakeCodePointRangeAndPos(std::span<encoded_char const> chars, size_t byteOffset) const noexcept
    {
        return CodePointIterator::MakeCodePointRangeAndPos(chars, byteOffset);
    }

    // Converts a sequence of code points into UTF-8 in-place (always fits).
    // Overwrites the input, starting at the beginning. Returns the UTF-8 buffer.
    std::span<encoded_char>
    ConvertInPlace(std::span<char32_t> codePoints) const noexcept
    {
        auto* const dst = reinterpret_cast<encoded_char*>(codePoints.data());
        size_t dstPos = 0;
        for (auto codePoint : codePoints)
        {
            dstPos += Encode(dst + dstPos, codePoint);
        }

        return std::span{ dst, dstPos };
    }

    // Encodes a single code point into UTF-8.
    // Returns the number of bytes written (1-4).
    // Encodes out-of-range code points (above 0x10FFFF) as the replacement character (U+FFFD).
    // Does not detect other invalid code points (e.g. surrogates).
    constexpr unsigned
    Encode(_Out_writes_to_(4, return) encoded_char* dst, char32_t codePoint) const noexcept
    {
        if (codePoint <= 0x7F)
        {
            dst[0] = static_cast<encoded_char>(codePoint);
            return 1;
        }
        else
        {
            return EncodeNonAscii(dst, codePoint);
        }
    }

private:

    static constexpr unsigned MyCodePage = 65001;

    // Encodes a single non-ascii code point into UTF-8.
    // Returns the number of bytes written (2-4).
    // Encodes out-of-range code points (above 0x10FFFF) as the replacement character (U+FFFD).
    // Does not detect other invalid code points (e.g. surrogates).
    static constexpr unsigned
    EncodeNonAscii(_Out_writes_to_(4, return) encoded_char* dst, char32_t codePoint) noexcept
    {
        assert(!CodePoint::IsAscii(codePoint));
        if (codePoint <= 0x7FF)
        {
            dst[0] = static_cast<encoded_char>(0xC0 | (codePoint >> 6));
            dst[1] = static_cast<encoded_char>(0x80 | (codePoint & 0x3F));
            return 2;
        }
        else if (codePoint <= 0xFFFF)
        {
            dst[0] = static_cast<encoded_char>(0xE0 | (codePoint >> 12));
            dst[1] = static_cast<encoded_char>(0x80 | ((codePoint >> 6) & 0x3F));
            dst[2] = static_cast<encoded_char>(0x80 | (codePoint & 0x3F));
            return 3;
        }
        else if (codePoint <= 0x10FFFF)
        {
            dst[0] = static_cast<encoded_char>(0xF0 | (codePoint >> 18));
            dst[1] = static_cast<encoded_char>(0x80 | ((codePoint >> 12) & 0x3F));
            dst[2] = static_cast<encoded_char>(0x80 | ((codePoint >> 6) & 0x3F));
            dst[3] = static_cast<encoded_char>(0x80 | (codePoint & 0x3F));
            return 4;
        }
        else
        {
            // U+FFFD replacement character
            dst[0] = static_cast<encoded_char>(0xEF);
            dst[1] = static_cast<encoded_char>(0xBF);
            dst[2] = static_cast<encoded_char>(0xBD);
            return 3;
        }
    }

    friend class CodePointIterator;

    // Implementation for Utf8::CodePointIterator.
    struct _codePointIterator
    {
        _Field_range_(m_begin, m_end) encoded_char const* m_pos;
        encoded_char const* m_begin;
        encoded_char const* m_end;

        static constexpr bool
        IsAsciiByte(encoded_char ch) noexcept
        {
            return static_cast<signed char>(ch) >= 0;
        }

        static constexpr bool
        IsLeadByte(encoded_char ch) noexcept
        {
            return static_cast<unsigned char>(ch - 0xC2) <= 0x32; // 0xC2-0xF4
        }

        static constexpr bool
        IsTrailByte(encoded_char ch) noexcept
        {
            return (ch & 0xC0) == 0x80;
        }

        // Returns the expected trail length for a lead byte (1-3), or 0 if invalid.
        static constexpr unsigned
        TrailLength(encoded_char lead) noexcept
        {
            return IsLeadByte(lead)
                ? (lead >= 0xe0) + (lead >= 0xf0) + 1
                : 0;
        }

        constexpr
        _codePointIterator(encoded_char const* pos, encoded_char const* begin, encoded_char const* end) noexcept
            : m_pos(pos)
            , m_begin(begin)
            , m_end(end)
        {}

        TEXTENCODING_FORCEINLINE
        constexpr char32_t
        Read() const noexcept
        {
            encoded_char const lead = m_pos[0];
            return IsAsciiByte(lead) ? lead : ReadNonAscii(lead);
        }

        constexpr char32_t
        ReadNonAscii(encoded_char const lead) const noexcept
        {
            unsigned const trailLength = TrailLength(lead);
            if (trailLength == 0 || m_pos + trailLength >= m_end)
            {
                return CodePoint::ReplacementChar;
            }

            // Verify all trail bytes are valid.
            for (unsigned i = 1; i <= trailLength; i += 1)
            {
                if (!IsTrailByte(m_pos[i]))
                {
                    return CodePoint::ReplacementChar;
                }
            }

            char32_t cp;
            switch (trailLength)
            {
            case 1:
                cp = (static_cast<char32_t>(lead & 0x1F) << 6)
                    | (static_cast<char32_t>(m_pos[1] & 0x3F));
                break;
            case 2:
                cp = (static_cast<char32_t>(lead & 0x0F) << 12)
                    | (static_cast<char32_t>(m_pos[1] & 0x3F) << 6)
                    | (static_cast<char32_t>(m_pos[2] & 0x3F));
                // Reject surrogates and overlong encodings.
                if (cp < 0x0800 || (cp >= 0xD800 && cp <= 0xDFFF))
                {
                    return CodePoint::ReplacementChar;
                }
                break;
            case 3:
                cp = (static_cast<char32_t>(lead & 0x07) << 18)
                    | (static_cast<char32_t>(m_pos[1] & 0x3F) << 12)
                    | (static_cast<char32_t>(m_pos[2] & 0x3F) << 6)
                    | (static_cast<char32_t>(m_pos[3] & 0x3F));
                // Reject overlong encodings and values above U+10FFFF.
                if (cp < 0x10000 || cp > 0x10FFFF)
                {
                    return CodePoint::ReplacementChar;
                }
                break;
            default:
                return CodePoint::ReplacementChar; // Unreachable.
            }

            return cp;
        }

        TEXTENCODING_FORCEINLINE
        constexpr void
        Increment() noexcept
        {
            encoded_char const lead = m_pos[0];
            if (IsAsciiByte(lead))
            {
                m_pos += 1;
            }
            else
            {
                IncrementNonAscii(lead);
            }
        }

        constexpr void
        IncrementNonAscii(encoded_char const lead) noexcept
        {
            unsigned const trailLength = TrailLength(lead);
            if (trailLength == 0)
            {
                // invalid lead byte: advance one byte.
                m_pos += 1;
            }
            else
            {
                // Advance past as many valid trail bytes as expected.
                unsigned advance = 1;
                for (; advance <= trailLength; advance += 1)
                {
                    if (m_pos + advance >= m_end || !IsTrailByte(m_pos[advance]))
                    {
                        break;
                    }
                }
                m_pos += advance;
            }
        }

        TEXTENCODING_FORCEINLINE
        constexpr void
        Decrement() noexcept
        {
            m_pos -= 1;
            auto const tail = m_pos[0];
            if (!IsAsciiByte(tail))
            {
                DecrementNonAscii();
            }
        }

        constexpr void
        DecrementNonAscii() noexcept
        {
            encoded_char const* p = m_pos;

            // Back up over trail bytes (max 3).
            unsigned trailCount = 0;
            for (; p > m_begin && IsTrailByte(*p) && trailCount < 3; trailCount += 1)
            {
                p -= 1;
            }

            // Check if p points to a valid lead byte that would consume these trail bytes.
            unsigned const trailLength = TrailLength(*p);
            if (trailLength > 0 && trailCount <= trailLength)
            {
                // Valid sequence: position at the lead byte.
                m_pos = p;
            }
        }
    };
};

template<bool BigEndian = false>
struct Utf16
{
    using encoded_char = char16_t;

    // A bidirectional iterator over UTF-16 data that dereferences to char32_t.
    // Handles surrogate pairs. Invalid sequences (lone surrogates) are returned as U+FFFD.
    using CodePointIterator = _textEncodingDetail::CodePointIterator<Utf16>;

    // A begin/end iterator pair, created by CodePointIterator::FromSpan.
    using CodePointRange = _textEncodingDetail::CodePointRange<CodePointIterator>;

    // A begin/end/pos iterator triple, created by CodePointIterator::FromSpanAndByteOffset.
    using CodePointRangeAndPos = _textEncodingDetail::CodePointRangeAndPos<CodePointIterator>;

    constexpr bool
    operator==(Utf16 const&) const noexcept
    {
        return true;
    }

    static std::optional<Utf16>
    TryFromCodePage(unsigned codePage) noexcept
    {
        if (codePage == MyCodePage)
        {
            return Utf16();
        }
        return std::nullopt;
    }

    static constexpr unsigned
    CodePage() noexcept
    {
        return MyCodePage;
    }

    // Returns a begin/end pair of CodePointIterators for the given char span.
    constexpr CodePointRange
    MakeCodePointRange(std::span<encoded_char const> chars) const noexcept
    {
        return CodePointIterator::MakeCodePointRange(chars);
    }

    // Returns begin and end like FromSpan.
    // Also returns a position iterator for begin + byteOffset, or CodePointIterator() if the
    // byteOffset is past the end of the span or if it points at an invalid position in the
    // input byte sequence (e.g. middle of a char16_t, middle of a UTF-8 sequence, etc.).
    constexpr CodePointRangeAndPos
    MakeCodePointRangeAndPos(std::span<encoded_char const> chars, size_t byteOffset) const noexcept
    {
        return CodePointIterator::MakeCodePointRangeAndPos(chars, byteOffset);
    }

    // Converts a sequence of code points into UTF-16 in-place (always fits).
    // Overwrites the input, starting at the beginning. Returns the UTF-16 buffer.
    std::span<encoded_char>
    ConvertInPlace(std::span<char32_t> codePoints) const noexcept
    {
        auto* const dst = reinterpret_cast<encoded_char*>(codePoints.data());
        size_t dstPos = 0;
        for (auto codePoint : codePoints)
        {
            dstPos += Encode(dst + dstPos, codePoint);
        }

        return std::span{ dst, dstPos };
    }

    // Encodes a single code point into UTF-16.
    // Returns the number of 16-bit code units written (1-2).
    // Encodes out-of-range code points (above 0x10FFFF) as the replacement character (U+FFFD).
    // Does not detect other invalid code points (e.g. surrogates).
    constexpr unsigned
    Encode(_Out_writes_to_(2, return) encoded_char* dst, char32_t codePoint) const
    {
        if (CodePoint::IsBmp(codePoint))
        {
            dst[0] = Swap16(static_cast<encoded_char>(codePoint));
            return 1;
        }
        else
        {
            return EncodeNonBmp(dst, codePoint);
        }
    }

    // Encodes a single non-BMP code point into UTF-16.
    // Returns the number of 16-bit code units written (1-2).
    // Encodes out-of-range code points (above 0x10FFFF) as the replacement character (U+FFFD).
    // Does not detect other invalid code points (e.g. surrogates).
    constexpr unsigned
    EncodeNonBmp(_Out_writes_to_(2, return) encoded_char* dst, char32_t supplementaryCodePoint) const
    {
        assert(!CodePoint::IsBmp(supplementaryCodePoint));
        if (CodePoint::IsCodePoint(supplementaryCodePoint))
        {
            dst[0] = Swap16(static_cast<encoded_char>((supplementaryCodePoint >> 10) + 0xD7C0));
            dst[1] = Swap16(static_cast<encoded_char>((supplementaryCodePoint & 0x3FF) | 0xDC00));
            return 2;
        }
        else
        {
            dst[0] = Swap16(static_cast<encoded_char>(CodePoint::ReplacementChar));
            return 1;
        }
    }

private:

    static constexpr unsigned MyCodePage = BigEndian ? 1201 : 1200;

    TEXTENCODING_FORCEINLINE
    static constexpr encoded_char
    Swap16(encoded_char ch16) noexcept
    {
        if constexpr (BigEndian)
        {
            return (ch16 >> 8) | (ch16 << 8);
        }
        else
        {
            return ch16;
        }
    }

    friend class CodePointIterator;

    // Implementation for Utf16::CodePointIterator.
    struct _codePointIterator
    {
        _Field_range_(m_begin, m_end) encoded_char const* m_pos;
        encoded_char const* m_begin;
        encoded_char const* m_end;

        constexpr
        _codePointIterator(encoded_char const* pos, encoded_char const* begin, encoded_char const* end) noexcept
            : m_pos(pos)
            , m_begin(begin)
            , m_end(end)
        {}

        TEXTENCODING_FORCEINLINE
        constexpr char32_t
        Read() const noexcept
        {
            encoded_char const first = Swap16(m_pos[0]);
            return first < 0xD800
                ? first
                : ReadComplex(first);
        }

        constexpr char32_t
        ReadComplex(encoded_char first) const noexcept
        {
            if (first < 0xDC00)
            {
                // High surrogate.
                if (m_pos + 1 < m_end)
                {
                    encoded_char const second = Swap16(m_pos[1]);
                    if (CodePoint::IsLowSurrogate(second))
                    {
                        return CodePoint::FromSurrogatePair(first, second);
                    }
                }

                // Invalid sequence.
            }
            else if (first < 0xE000)
            {
                // Low surrogate without preceding high surrogate is invalid.
            }
            else
            {
                return first;
            }

            return CodePoint::ReplacementChar;
        }

        TEXTENCODING_FORCEINLINE
        constexpr void
        Increment() noexcept
        {
            encoded_char const first = Swap16(m_pos[0]);
            m_pos += 1;
            if (first >= 0xD800)
            {
                IncrementComplex(first);
            }
        }

        constexpr void
        IncrementComplex(encoded_char first) noexcept
        {
            if (first < 0xDC00)
            {
                // High surrogate, skip low surrogate if valid.
                if (m_pos < m_end && CodePoint::IsLowSurrogate(Swap16(m_pos[0])))
                {
                    m_pos += 1;
                }
            }
        }

        constexpr void
        Decrement() noexcept
        {
            m_pos -= 1;
            if (m_pos > m_begin && CodePoint::IsLowSurrogate(Swap16(m_pos[0])))
            {
                encoded_char const* prev = m_pos - 1;
                if (prev >= m_begin && CodePoint::IsHighSurrogate(Swap16(prev[0])))
                {
                    m_pos = prev;
                }
            }
        }
    };
};

// Conversions between utf-16le (cp1200) and 32-bit code points.
using Utf16LE = Utf16<false>;

// Conversions between utf-16be (cp1201) and 32-bit code points.
using Utf16BE = Utf16<true>;

using TextEncoding = std::variant<
    Latin1,
    Utf8,
    Utf16LE,
    Utf16BE,
    Sbcs>;

// If codePage is supported by any of the encodings, returns func(encoding).
// Otherwise, returns false.
template<class FuncT>
_Success_(return)
inline bool
VisitEncodingForCodePage(unsigned codePage, FuncT&& func)
{
    switch (codePage)
    {
    case Latin1::CodePage():
        return func(Latin1{});
    case Utf8::CodePage():
        return func(Utf8{});
    case Utf16LE::CodePage():
        return func(Utf16LE{});
    case Utf16BE::CodePage():
        return func(Utf16BE{});
    default:
        if (auto const encoding = Sbcs::TryFromCodePage(codePage))
        {
            return func(*encoding);
        }
        else
        {
            return false;
        }
    }
}

// If codePage is supported by any of the encodings, sets *encoding = TextEncoding(encoding)
// and returns true. Otherwise, returns false.
_Success_(return)
inline bool
TextEncodingForCodePage(unsigned codePage, _Out_ TextEncoding* pEncoding) noexcept
{
    return VisitEncodingForCodePage(codePage, [pEncoding](auto encoding) {
        *pEncoding = encoding;
        return true;
        });
}
