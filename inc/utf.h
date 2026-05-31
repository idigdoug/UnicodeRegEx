#pragma once
#include <span>
#include <assert.h>

// Utilities for 32-bit code points.
struct utf
{
    static constexpr char32_t ReplacementChar = 0xFFFD;

    // codePoint <= 0x7F
    static constexpr bool
    IsAscii(char32_t codePoint) noexcept
    {
        return codePoint <= 0x7F;
    }

    // codePoint <= 0xFF
    static constexpr bool
    IsLatin1(char32_t codePoint) noexcept
    {
        return codePoint <= 0xFF;
    }

    // codePoint <= 0xFFFF
    static constexpr bool
    IsBmp(char32_t codePoint) noexcept
    {
        return codePoint <= 0xFFFF;
    }

    // codePoint <= 0x10FFFF
    static constexpr bool
    IsCodePoint(char32_t codePoint) noexcept
    {
        return codePoint <= 0x10FFFF;
    }

    // codePoint >= 0xD800 && codePoint <= 0xDC00
    static constexpr bool
    IsHighSurrogate(char32_t ch) noexcept
    {
        return (ch & 0xFFFFFC00) == 0xD800;
    }

    // codePoint >= 0xDC00 && codePoint <= 0xDFFF
    static constexpr bool
    IsLowSurrogate(char32_t ch) noexcept
    {
        return (ch & 0xFFFFFC00) == 0xDC00;
    }

    // Convert a surrogate pair to a supplementary-plane code point.
    static constexpr char32_t
    FromSurrogatePair(char16_t high, char16_t low) noexcept
    {
        assert(0xD800 <= high && high <= 0xDBFF);
        assert(0xDC00 <= low && low <= 0xDFFF);
        return ((static_cast<char32_t>(high) - 0xD800) << 10) + (low - 0xDC00) + 0x10000;
    }
};

// Conversions between latin1 (ucs1) and 32-bit code points.
struct latin1
{
    // A random-access iterator over char data that dereferences to char32_t (Latin-1 identity mapping).
    class CodePointIterator
    {
        char const* m_pos;

        constexpr explicit
        CodePointIterator(char const* pos) noexcept
            : m_pos(pos)
        {}

    public:

        using iterator_category = std::random_access_iterator_tag;
        using value_type = char32_t;
        using difference_type = std::ptrdiff_t;
        using pointer = char32_t const*;
        using reference = char32_t;
        using input_type = char;

        static constexpr std::pair<CodePointIterator, CodePointIterator>
        FromSpan(std::span<input_type const> chars) noexcept
        {
            auto const begin = chars.data();
            auto const end = chars.data() + chars.size();
            return { CodePointIterator(begin), CodePointIterator(end) };
        }

        constexpr
        CodePointIterator() noexcept
            : m_pos(nullptr)
        {}

        // Returns the number of bytes between begin and the current iterator position.
        // The begin value should be chars.data() from the chars value passed to FromSpan.
        constexpr size_t
        ByteOffset(void const* begin) const noexcept
        {
            assert(m_pos >= static_cast<char const*>(begin));
            return (m_pos - static_cast<char const*>(begin)) * sizeof(char);
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

    // Encodes a single code point into Latin1.
    // Returns the number of bytes written (always 1).
    // Encodes out-of-range code points (above 0xFF) as '?'.
    static constexpr unsigned
    Encode(_Out_writes_all_(1) char* dst, char32_t codePoint) noexcept
    {
        dst[0] = utf::IsLatin1(codePoint) ? static_cast<char>(codePoint) : '?';
        return 1;
    }

    // Converts a sequence of code points into a latin1 string.
    // Overwrites the input (always fits). Returns the latin1 buffer.
    static std::span<char>
    ConvertInPlace(std::span<char32_t> codePoints)
    {
        auto* const dst = reinterpret_cast<char*>(codePoints.data());
        size_t dstPos = 0;
        for (auto codePoint : codePoints)
        {
            dstPos += Encode(dst + dstPos, codePoint);
        }

        return std::span{ dst, dstPos };
    }
};

// Conversions between utf-8 and 32-bit code points.
struct utf8
{
    class CodePointIterator
    {
        _Field_range_(m_begin, m_end) char8_t const* m_pos;
        char8_t const* m_begin;
        char8_t const* m_end;

        constexpr
        CodePointIterator(char8_t const* begin, char8_t const* end, char8_t const* pos) noexcept
            : m_pos(pos)
            , m_begin(begin)
            , m_end(end)
        {}

        static constexpr bool
        IsTrailByte(char8_t ch) noexcept
        {
            return (ch & 0xC0) == 0x80;
        }

        // Returns the expected trail length for a lead byte (1-3), or 0 if invalid.
        static constexpr unsigned
        TrailLength(char8_t lead) noexcept
        {
            if (lead < 0xC2) return 0; // trail bytes and overlong 2-byte sequences
            if (lead < 0xE0) return 1;
            if (lead < 0xF0) return 2;
            if (lead < 0xF5) return 3;
            return 0; // F5+ is invalid
        }

    public:

        using iterator_category = std::bidirectional_iterator_tag;
        using value_type = char32_t;
        using difference_type = std::ptrdiff_t;
        using pointer = char32_t const*;
        using reference = char32_t;
        using input_type = char8_t;

        static constexpr std::pair<CodePointIterator, CodePointIterator>
        FromSpan(std::span<input_type const> chars) noexcept
        {
            auto const begin = chars.data();
            auto const end = chars.data() + chars.size();
            return { CodePointIterator(begin, end, begin), CodePointIterator(begin, end, end) };
        }

        constexpr
        CodePointIterator() noexcept
            : m_pos(nullptr)
            , m_begin(nullptr)
            , m_end(nullptr)
        {}

        // Returns the number of bytes between begin and the current iterator position.
        // The begin value should be chars.data() from the chars value passed to FromSpan.
        constexpr size_t
        ByteOffset(void const* begin) const noexcept
        {
            assert(m_begin == static_cast<char8_t const*>(begin));
            assert(m_pos >= static_cast<char8_t const*>(begin));
            return (m_pos - static_cast<char8_t const*>(begin)) * sizeof(char8_t);
        }

        constexpr value_type
        operator*() const noexcept
        {
            assert(m_begin <= m_pos && m_pos < m_end);

            char8_t const lead = m_pos[0];
            if (lead < 0x80)
            {
                return lead;
            }

            unsigned const trailLength = TrailLength(lead);
            if (trailLength == 0 || m_pos + trailLength >= m_end)
            {
                return utf::ReplacementChar;
            }

            // Verify all trail bytes are valid.
            for (unsigned i = 1; i <= trailLength; i += 1)
            {
                if (!IsTrailByte(m_pos[i]))
                {
                    return utf::ReplacementChar;
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
                    return utf::ReplacementChar;
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
                    return utf::ReplacementChar;
                }
                break;
            default:
                return utf::ReplacementChar; // Unreachable.
            }

            return cp;
        }

        constexpr CodePointIterator&
        operator++() noexcept
        {
            assert(m_begin <= m_pos && m_pos < m_end);

            char8_t const lead = m_pos[0];
            if (lead < 0x80)
            {
                m_pos += 1;
            }
            else
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

            return *this;
        }

        constexpr CodePointIterator
        operator++(int) noexcept
        {
            auto tmp = *this;
            ++*this;
            return tmp;
        }

        constexpr CodePointIterator&
        operator--() noexcept
        {
            assert(m_begin < m_pos && m_pos <= m_end);

            char8_t const* p = m_pos - 1;
            if (*p < 0x80)
            {
                m_pos = p;
            }
            else
            {
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
                else
                {
                    // Invalid sequence: back up only one byte.
                    m_pos -= 1;
                }
            }

            return *this;
        }

        constexpr CodePointIterator
        operator--(int) noexcept
        {
            auto tmp = *this;
            --*this;
            return tmp;
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
    };

    // Encodes a single non-ascii code point into UTF-8.
    // Returns the number of bytes written (2-4).
    // Encodes out-of-range code points (above 0x10FFFF) as the replacement character (U+FFFD).
    // Does not detect other invalid code points (e.g. surrogates).
    static constexpr unsigned
    EncodeNonAscii(_Out_writes_to_(4, return) char8_t* dst, char32_t codePoint) noexcept
    {
        assert(!utf::IsAscii(codePoint));
        if (codePoint <= 0x7FF)
        {
            dst[0] = static_cast<char8_t>(0xC0 | (codePoint >> 6));
            dst[1] = static_cast<char8_t>(0x80 | (codePoint & 0x3F));
            return 2;
        }
        else if (codePoint <= 0xFFFF)
        {
            dst[0] = static_cast<char8_t>(0xE0 | (codePoint >> 12));
            dst[1] = static_cast<char8_t>(0x80 | ((codePoint >> 6) & 0x3F));
            dst[2] = static_cast<char8_t>(0x80 | (codePoint & 0x3F));
            return 3;
        }
        else if (codePoint <= 0x10FFFF)
        {
            dst[0] = static_cast<char8_t>(0xF0 | (codePoint >> 18));
            dst[1] = static_cast<char8_t>(0x80 | ((codePoint >> 12) & 0x3F));
            dst[2] = static_cast<char8_t>(0x80 | ((codePoint >> 6) & 0x3F));
            dst[3] = static_cast<char8_t>(0x80 | (codePoint & 0x3F));
            return 4;
        }
        else
        {
            // U+FFFD replacement character
            dst[0] = static_cast<char8_t>(0xEF);
            dst[1] = static_cast<char8_t>(0xBF);
            dst[2] = static_cast<char8_t>(0xBD);
            return 3;
        }
    }

    // Encodes a single code point into UTF-8.
    // Returns the number of bytes written (1-4).
    // Encodes out-of-range code points (above 0x10FFFF) as the replacement character (U+FFFD).
    // Does not detect other invalid code points (e.g. surrogates).
    static constexpr unsigned
    Encode(_Out_writes_to_(4, return) char8_t* dst, char32_t codePoint) noexcept
    {
        if (codePoint <= 0x7F)
        {
            dst[0] = static_cast<char8_t>(codePoint);
            return 1;
        }
        else
        {
            return EncodeNonAscii(dst, codePoint);
        }
    }

    // Converts a sequence of code points into UTF-8 in-place (always fits).
    // Overwrites the input, starting at the beginning. Returns the UTF-8 buffer.
    static std::span<char8_t>
    ConvertInPlace(std::span<char32_t> codePoints)
    {
        auto* const dst = reinterpret_cast<char8_t*>(codePoints.data());
        size_t dstPos = 0;
        for (auto codePoint : codePoints)
        {
            dstPos += Encode(dst + dstPos, codePoint);
        }

        return std::span{ dst, dstPos };
    }
};

template<bool BigEndian = false>
struct utf16
{
    // A bidirectional iterator over UTF-16 data that dereferences to char32_t.
    // Handles surrogate pairs. Invalid sequences (lone surrogates) are returned as U+FFFD.
    class CodePointIterator
    {
        _Field_range_(m_begin, m_end) char16_t const* m_pos;
        char16_t const* m_begin;
        char16_t const* m_end;

        constexpr
        CodePointIterator(char16_t const* begin, char16_t const* end, char16_t const* pos) noexcept
            : m_pos(pos)
            , m_begin(begin)
            , m_end(end)
        {}

    public:

        using iterator_category = std::bidirectional_iterator_tag;
        using value_type = char32_t;
        using difference_type = std::ptrdiff_t;
        using pointer = char32_t const*;
        using reference = char32_t;
        using input_type = char16_t;

        static constexpr std::pair<CodePointIterator, CodePointIterator>
        FromSpan(std::span<input_type const> chars) noexcept
        {
            auto const begin = chars.data();
            auto const end = chars.data() + chars.size();
            return { CodePointIterator(begin, end, begin), CodePointIterator(begin, end, end) };
        }

        constexpr
        CodePointIterator() noexcept
            : m_pos(nullptr)
            , m_begin(nullptr)
            , m_end(nullptr)
        {}

        // Returns the number of bytes between begin and the current iterator position.
        // The begin value should be chars.data() from the chars value passed to FromSpan.
        constexpr size_t
        ByteOffset(void const* begin) const noexcept
        {
            assert(m_begin == static_cast<char16_t const*>(begin));
            assert(m_pos >= static_cast<char16_t const*>(begin));
            return (m_pos - static_cast<char16_t const*>(begin)) * sizeof(char16_t);
        }

        constexpr value_type
        operator*() const noexcept
        {
            assert(m_begin <= m_pos && m_pos < m_end);

            char16_t const ch = Swap16(m_pos[0]);
            if (ch < 0xD800)
            {
                return ch;
            }
            else if (ch < 0xDC00)
            {
                // High surrogate.
                if (m_pos + 1 < m_end)
                {
                    char16_t const low = Swap16(m_pos[1]);
                    if (utf::IsLowSurrogate(low))
                    {
                        return utf::FromSurrogatePair(ch, low);
                    }
                }

                // Invalid sequence.
            }
            else if (ch < 0xE000)
            {
                // Low surrogate without preceding high surrogate is invalid.
            }
            else
            {
                return ch;
            }

            return utf::ReplacementChar;
        }

        constexpr CodePointIterator&
        operator++() noexcept
        {
            assert(m_begin <= m_pos && m_pos < m_end);

            char16_t const ch = Swap16(m_pos[0]);
            m_pos += 1;
            if (ch < 0xD800)
            {
                // Single code unit, done.
            }
            else if (ch < 0xDC00)
            {
                // High surrogate, skip low surrogate if valid.
                if (m_pos < m_end && utf::IsLowSurrogate(Swap16(m_pos[0])))
                {
                    m_pos += 1;
                }
            }
            else
            {
                // Single code unit or unpaired low surrogate, done.
            }

            return *this;
        }

        constexpr CodePointIterator
        operator++(int) noexcept
        {
            auto tmp = *this;
            ++*this;
            return tmp;
        }

        constexpr CodePointIterator&
        operator--() noexcept
        {
            assert(m_begin < m_pos && m_pos <= m_end);

            m_pos -= 1;
            if (m_pos > m_begin && utf::IsLowSurrogate(Swap16(m_pos[0])))
            {
                char16_t const* prev = m_pos - 1;
                if (prev >= m_begin && utf::IsHighSurrogate(Swap16(prev[0])))
                {
                    m_pos = prev;
                }
            }
            return *this;
        }

        constexpr CodePointIterator
        operator--(int) noexcept
        {
            auto tmp = *this;
            --*this;
            return tmp;
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
    };

    // Encodes a single non-BMP code point into UTF-16.
    // Returns the number of 16-bit code units written (1-2).
    // Encodes out-of-range code points (above 0x10FFFF) as the replacement character (U+FFFD).
    // Does not detect other invalid code points (e.g. surrogates).
    static constexpr unsigned
    EncodeNonBmp(_Out_writes_to_(2, return) char16_t* dst, char32_t supplementaryCodePoint)
    {
        assert(!utf::IsBmp(supplementaryCodePoint));
        if (utf::IsCodePoint(supplementaryCodePoint))
        {
            dst[0] = Swap16(static_cast<char16_t>((supplementaryCodePoint >> 10) + 0xD7C0));
            dst[1] = Swap16(static_cast<char16_t>((supplementaryCodePoint & 0x3FF) | 0xDC00));
            return 2;
        }
        else
        {
            dst[0] = Swap16(static_cast<char16_t>(utf::ReplacementChar));
            return 1;
        }
    }

    // Encodes a single code point into UTF-16.
    // Returns the number of 16-bit code units written (1-2).
    // Encodes out-of-range code points (above 0x10FFFF) as the replacement character (U+FFFD).
    // Does not detect other invalid code points (e.g. surrogates).
    static constexpr unsigned
    Encode(_Out_writes_to_(2, return) char16_t* dst, char32_t codePoint)
    {
        if (utf::IsBmp(codePoint))
        {
            dst[0] = Swap16(static_cast<char16_t>(codePoint));
            return 1;
        }
        else
        {
            return EncodeNonBmp(dst, codePoint);
        }
    }

    // Converts a sequence of code points into UTF-16 in-place (always fits).
    // Overwrites the input, starting at the beginning. Returns the UTF-16 buffer.
    static std::span<char16_t>
    ConvertInPlace(std::span<char32_t> codePoints)
    {
        auto* const dst = reinterpret_cast<char16_t*>(codePoints.data());
        size_t dstPos = 0;
        for (auto codePoint : codePoints)
        {
            dstPos += Encode(dst + dstPos, codePoint);
        }

        return std::span{ dst, dstPos };
    }

private:

    static constexpr char16_t
    Swap16(char16_t ch16) noexcept
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
};

using utf16le = utf16<false>;
using utf16be = utf16<true>;
