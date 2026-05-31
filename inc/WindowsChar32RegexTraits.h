#pragma once
#include <boost/regex.hpp>

class WindowsChar32RegexTraits
{
    static constexpr unsigned AsciiTableMax = 128;

    // These locales can use the hard-coded ASCII lowercasing table during imbue()
    // instead of querying the OS.
    static constexpr unsigned short KnownLatinCompatibleLcids[] = {
        0,      // LOCALE_NEUTRAL
        0x7F,   // LOCALE_INVARIANT
        0x0409, // LOCALE_ENGLISH_US
        0x0809, // LOCALE_ENGLISH_UK
        0x0C09, // LOCALE_ENGLISH_AUS
        0x1009  // LOCALE_ENGLISH_CAN
    };

    unsigned long m_lcid;
    wchar_t m_asciiToLower[AsciiTableMax];

public:

    // The character container type used in the implementation of class template basic_regex.
    using char_type = char32_t;

    // An unsigned integer type, capable of holding the length of a null-terminated string of charT's.
    using size_type = std::size_t;

    // std::basic_string<charT> or std::vector<charT>
    using string_type = std::basic_string<char_type>;

    // A copy constructible type that represents the locale used by the traits class.
    using locale_type = unsigned long;

    // A bitmask type representing a particular character classification. Multiple values of this type
    // can be bitwise-or'ed together to obtain a new valid value.
    using char_class_type = unsigned short;

    // When present, all of the extensions must be present.
    using boost_extensions_tag = int;

    // Initialize for LOCALE_NEUTRAL.
    constexpr
    WindowsChar32RegexTraits() noexcept
        : m_lcid{}
        , m_asciiToLower{}
    {
        AsciiToLowerInitLatin();
    }

    // Yields the smallest i such that p[i] == 0. Complexity is linear in i.
    static constexpr size_type
    length(_In_z_ char_type const* pch) noexcept
    {
        return ::std::char_traits<char_type>::length(pch);
    }

    // Returns a character such that for any character d that is to be
    // considered equivalent to c then v.translate(c) == v.translate(d).
    constexpr char_type
    translate(char_type ch) const noexcept
    {
        return ch;
    }

    // For all characters C that are to be considered equivalent to c when
    // comparisons are to be performed without regard to case, then
    // v.translate_nocase(c) == v.translate_nocase(C).
    char_type
    translate_nocase(char_type ch) const noexcept;

    // Returns a sort key for the character sequence designated by the iterator
    // range [F1, F2) such that if the character sequence [G1, G2) sorts before
    // the character sequence [H1, H2) then v.transform(G1, G2) < v.transform(H1, H2).
    string_type
    transform(_In_reads_to_ptr_(pchEnd) char_type const* pchBegin, char_type const* pchEnd) const;

    // std::regex_traits compatible template overload.
    template<typename ForwardIt>
    string_type
    transform(ForwardIt first, ForwardIt last) const
    {
        string_type const s(first, last);
        return transform(s.data(), s.data() + s.size());
    }

    // Returns a sort key for the character sequence designated by the iterator
    // range [F1, F2) such that if the character sequence [G1, G2) sorts before the
    // character sequence [H1, H2) when character case is not considered then
    // v.transform_primary(G1, G2) < v.transform_primary(H1, H2).
    string_type
    transform_primary(_In_reads_to_ptr_(pchEnd) char_type const* pchBegin, char_type const* pchEnd) const;

    // std::regex_traits compatible template overload.
    template<typename ForwardIt>
    string_type
    transform_primary(ForwardIt first, ForwardIt last) const
    {
        string_type const s(first, last);
        return transform_primary(s.data(), s.data() + s.size());
    }

    // Converts the character sequence designated by the iterator range [F1,F2) into a
    // bitmask type that can subsequently be passed to isctype. Values returned from
    // lookup_classname can be safely bitwise or'ed together. Returns 0 if the character
    // sequence is not the name of a character class recognized by X. The value returned
    // shall be independent of the case of the characters in the sequence.
    char_class_type
    lookup_classname(_In_reads_to_ptr_(pchEnd) char_type const* pchBegin, char_type const* pchEnd) const noexcept;

    // std::regex_traits compatible template overload (icase parameter is ignored since
    // lookup_classname is already case-insensitive).
    template<typename ForwardIt>
    char_class_type
    lookup_classname(ForwardIt first, ForwardIt last, bool /*icase*/ = false) const noexcept
    {
        string_type const s(first, last);
        return lookup_classname(s.data(), s.data() + s.size());
    }

    // Returns a sequence of characters that represents the collating element consisting
    // of the character sequence designated by the iterator range [F1, F2). Returns an
    // empty string if the character sequence is not a valid collating element.
    string_type
    lookup_collatename(_In_reads_to_ptr_(pchEnd) char_type const* pchBegin, char_type const* pchEnd) const;

    // std::regex_traits compatible template overload.
    template<typename ForwardIt>
    string_type
    lookup_collatename(ForwardIt first, ForwardIt last) const
    {
        string_type const s(first, last);
        return lookup_collatename(s.data(), s.data() + s.size());
    }

    // Returns true if character c is a member of the character class designated by the
    // iterator range [F1, F2), false otherwise.
    bool
    isctype(char_type ch, char_class_type ctype) const noexcept;

    // Returns the value represented by the digit c in base I if the character c is a valid
    // digit in base I; otherwise returns -1. [Note: the value of I will only be 8, 10,
    // or 16. -end note]
    int
    value(char_type ch, int radix) const noexcept;

    // Imbues u with the locale loc, returns the previous locale used by u if any.
    locale_type
    imbue(locale_type loc);

    // Returns the current locale used by v if any.
    locale_type
    getloc() const noexcept;

    // Returns a symbolic value of type regex_constants::syntax_type that signifies the
    // meaning of character c within the regular expression grammar.
    ::boost::regex_constants::syntax_type
    syntax_type(char_type ch) const noexcept;

    // Returns a symbolic value of type regex_constants::escape_syntax_type, that signifies
    // the meaning of character c within the regular expression grammar, when c has been
    // preceded by an escape character. Precondition: if b is the character preceding c in
    // the expression being parsed then: v.syntax_type(b) == syntax_escape
    ::boost::regex_constants::escape_syntax_type
    escape_syntax_type(char_type ch) const noexcept;

    // Returns a character d such that: for any character d that is to be considered
    // equivalent to c then v.translate(c,false)==v.translate(d,false). Likewise for all
    // characters C that are to be considered equivalent to c when comparisons are to be
    // performed without regard to case, then v.translate(c,true)==v.translate(C,true).
    char_type
    translate(char_type ch, bool icase) const noexcept;

    // Behaves as follows: if p == q or if *p is not a digit character then returns -1.
    // Otherwise performs formatted numeric input on the sequence [p,q) and returns the
    // result as an int. Postcondition: either p == q or *p is a non-digit character.
    std::intmax_t
    toi(char_type const*& p1, char_type const* p2, int radix) const noexcept;

    // Returns a human readable error string for the error condition i, where i is one
    // of the values enumerated by type regex_constants::error_type. If the value I is
    // not recognized then returns the string "Unknown error" or a localized equivalent.
    _Ret_z_ char const*
    error_string(::boost::regex_constants::error_type e) const noexcept;

    // Converts c to lower case, used for Perl-style \l and \L formatting operations.
    char_type
    tolower(char_type ch) const noexcept;

    // Converts c to upper case, used for Perl-style \u and \U formatting operations.
    char_type
    toupper(char_type ch) const noexcept;

    static WindowsChar32RegexTraits::char_class_type
    GetCharClass(char32_t ch, WindowsChar32RegexTraits::char_class_type filter) noexcept;

    // For testing purposes.
    static bool
    HardcodedTablesOk();

private:

    string_type
    TransformImpl(_In_reads_to_ptr_(pchEnd) char_type const* pchBegin, char_type const* pchEnd, unsigned mapFlags) const;

    char_type
    ToUpperLowerImpl(char_type ch, unsigned mapFlags) const noexcept;

    // If LCMapStringW(loc, LCMAP_LOWERCASE, ...) succeeds, initializes m_asciiToLower and m_lcid.
    // Throws std::runtime_error if LCMapStringW fails.
    void
    AsciiToLowerInit(locale_type loc);

    constexpr void
    AsciiToLowerInitLatin()
    {
        constexpr char unsigned LatinAsciiToLower[AsciiTableMax] =
        {
            // 0x00-0x0F: control characters
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
            // 0x10-0x1F: control characters
            0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F,
            // 0x20-0x2F: space and punctuation
            ' ',  '!',  '"',  '#',  '$',  '%',  '&',  '\'', '(',  ')',  '*',  '+',  ',',  '-',  '.',  '/',
            // 0x30-0x3F: digits and punctuation
            '0',  '1',  '2',  '3',  '4',  '5',  '6',  '7',  '8',  '9',  ':',  ';',  '<',  '=',  '>',  '?',
            // 0x40-0x4F: @ and A-O (mapped to a-o)
            '@',  'a',  'b',  'c',  'd',  'e',  'f',  'g',  'h',  'i',  'j',  'k',  'l',  'm',  'n',  'o',
            // 0x50-0x5F: P-Z (mapped to p-z), punctuation, _
            'p',  'q',  'r',  's',  't',  'u',  'v',  'w',  'x',  'y',  'z',  '[',  '\\', ']',  '^',  '_',
            // 0x60-0x6F: ` and a-o
            '`',  'a',  'b',  'c',  'd',  'e',  'f',  'g',  'h',  'i',  'j',  'k',  'l',  'm',  'n',  'o',
            // 0x70-0x7F: p-z, punctuation, DEL
            'p',  'q',  'r',  's',  't',  'u',  'v',  'w',  'x',  'y',  'z',  '{',  '|',  '}',  '~',  0x7F,
        };

        for (wchar_t i = 0; i != AsciiTableMax; i += 1)
        {
            m_asciiToLower[i] = LatinAsciiToLower[i];
        }
    }
};
