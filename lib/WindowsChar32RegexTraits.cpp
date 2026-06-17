#include "pch.h"
#include <WindowsChar32RegexTraits.h>
#include <string_view>

#include <TextEncoding.h>

using namespace std::string_view_literals;

static constexpr WORD CP_UTF32LE = 12000;

static constexpr WORD C1x_UNDERSCORE = 0x400;
static constexpr WORD C1x_EXTENDED = 0x800;
static constexpr WORD C1x_HORIZONTAL = 0x1000;
static constexpr WORD C1x_VERTICAL = 0x2000;
static constexpr WORD C1_ALL =
    C1_UPPER | // Uppercase
    C1_LOWER | // Lowercase
    C1_DIGIT | // Decimal digit
    C1_SPACE | // Space character
    C1_PUNCT | // Punctuation character
    C1_CNTRL | // Control character
    C1_BLANK | // Blank character
    C1_XDIGIT |// Hexadecimal digit
    C1_ALPHA;  // Linguistic: alphabetical, syllabary, or ideographic.

// Short aliases for readability in the table below.
static constexpr WORD Cc = C1_CNTRL | C1_DEFINED;
static constexpr WORD Cs = C1_CNTRL | C1_SPACE | C1_DEFINED;
static constexpr WORD Sb = C1_SPACE | C1_BLANK | C1_DEFINED;
static constexpr WORD Pp = C1_PUNCT | C1_DEFINED;
static constexpr WORD Dx = C1_DIGIT | C1_XDIGIT | C1_DEFINED;
static constexpr WORD Ua = C1_UPPER | C1_ALPHA | C1_DEFINED;
static constexpr WORD Ux = C1_UPPER | C1_ALPHA | C1_XDIGIT | C1_DEFINED;
static constexpr WORD La = C1_LOWER | C1_ALPHA | C1_DEFINED;
static constexpr WORD Lx = C1_LOWER | C1_ALPHA | C1_XDIGIT | C1_DEFINED;
static constexpr WORD Pu = C1_PUNCT | C1x_UNDERSCORE | C1_DEFINED;
static constexpr WORD Hh = C1x_HORIZONTAL;
static constexpr WORD Vv = C1x_VERTICAL;

// Precomputed character class flags for ASCII 0-127.
static constexpr WindowsChar32RegexTraits::char_class_type AsciiCharClass[] =
{
    // 0x00-0x0F
    Cc,      Cc,      Cc,      Cc,      Cc,      Cc,      Cc,      Cc,      // NUL-BEL
    Cc,      Cs|Sb|Hh,Cs|Vv,   Cs|Vv,   Cs|Vv,   Cs|Vv,   Cc,      Cc,      // BS, TAB, LF, VT, FF, CR, SO, SI
    // 0x10-0x1F
    Cc,      Cc,      Cc,      Cc,      Cc,      Cc,      Cc,      Cc,      // DLE-ETB
    Cc,      Cc,      Cc,      Cc,      Cc,      Cc,      Cc,      Cc,      // CAN-US
    // 0x20-0x2F
    Sb|Hh,   Pp,      Pp,      Pp,      Pp,      Pp,      Pp,      Pp,      // SP ! " # $ % & '
    Pp,      Pp,      Pp,      Pp,      Pp,      Pp,      Pp,      Pp,      // ( ) * + , - . /
    // 0x30-0x3F
    Dx,      Dx,      Dx,      Dx,      Dx,      Dx,      Dx,      Dx,      // 0-7
    Dx,      Dx,      Pp,      Pp,      Pp,      Pp,      Pp,      Pp,      // 8 9 : ; < = > ?
    // 0x40-0x4F
    Pp,      Ux,      Ux,      Ux,      Ux,      Ux,      Ux,      Ua,      // @ A-F G
    Ua,      Ua,      Ua,      Ua,      Ua,      Ua,      Ua,      Ua,      // H-O
    // 0x50-0x5F
    Ua,      Ua,      Ua,      Ua,      Ua,      Ua,      Ua,      Ua,      // P-W
    Ua,      Ua,      Ua,      Pp,      Pp,      Pp,      Pp,      Pu,      // X Y Z [ \ ] ^ _
    // 0x60-0x6F
    Pp,      Lx,      Lx,      Lx,      Lx,      Lx,      Lx,      La,      // ` a-f g
    La,      La,      La,      La,      La,      La,      La,      La,      // h-o
    // 0x70-0x7F
    La,      La,      La,      La,      La,      La,      La,      La,      // p-w
    La,      La,      La,      Pp,      Pp,      Pp,      Pp,      Cc,      // x y z { | } ~ DEL
};
static constexpr unsigned AsciiCharClassMax = sizeof(AsciiCharClass) / sizeof(AsciiCharClass[0]);
static_assert(AsciiCharClassMax == 128, "The size of AsciiCharClass must be 128.");

static bool
EqualAsciiIgnoreCase(std::u32string_view lhs, std::u32string_view rhs)
{
    auto const lhsSize = lhs.size();

    if (lhsSize != rhs.size())
    {
        return false;
    }

    for (std::size_t i = 0; i != lhsSize; i += 1)
    {
        char32_t const lhsChar = lhs[i];
		char32_t const lhsCharLower = lhsChar >= U'A' && lhsChar <= U'Z'
			? lhsChar | 0x20
			: lhsChar;
		char32_t const rhsChar = rhs[i];
		char32_t const rhsCharLower = rhsChar >= U'A' && rhsChar <= U'Z'
			? rhsChar | 0x20
			: rhsChar;
        if (lhsCharLower != rhsCharLower)
        {
            return false;
        }
    }

    return true;
}

static bool
EqualChar8Char32(std::string_view lhs, std::u32string_view rhs)
{
    auto const lhsSize = lhs.size();

    if (lhsSize != rhs.size())
    {
        return false;
    }

    for (std::size_t i = 0; i != lhsSize; i += 1)
    {
        if (static_cast<unsigned char>(lhs[i]) != rhs[i])
        {
            return false;
        }
    }

    return true;
}

WindowsChar32RegexTraits::char_type
WindowsChar32RegexTraits::translate_nocase(char_type ch) const noexcept
{
    return this->tolower(ch);
}

WindowsChar32RegexTraits::string_type
WindowsChar32RegexTraits::transform(_In_reads_to_ptr_(pchEnd) char_type const* pchBegin, char_type const* pchEnd) const
{
    return TransformImpl(pchBegin, pchEnd, LCMAP_SORTKEY);
}

WindowsChar32RegexTraits::string_type
WindowsChar32RegexTraits::transform_primary(_In_reads_to_ptr_(pchEnd) char_type const* pchBegin, char_type const* pchEnd) const
{
    return TransformImpl(pchBegin, pchEnd, LCMAP_SORTKEY | NORM_IGNORECASE);
}

WindowsChar32RegexTraits::char_class_type
WindowsChar32RegexTraits::lookup_classname(_In_reads_to_ptr_(pchEnd) char_type const* pchBegin, char_type const* pchEnd) const noexcept
{
    std::u32string_view className(pchBegin, pchEnd - pchBegin);

    struct ClassFlags
    {
        std::u32string_view name;
        char_class_type type;
    };

    static ClassFlags constexpr classFlags[] =
    {
        { U"alnum"sv,   C1_ALPHA | C1_DIGIT },
        { U"alpha"sv,   C1_ALPHA },
        { U"blank"sv,   C1_BLANK },
        { U"cntrl"sv,   C1_CNTRL },
        { U"d"sv,       C1_DIGIT },
        { U"digit"sv,   C1_DIGIT },
        { U"graph"sv,   (C1_ALL & ~(C1_CNTRL | C1_SPACE | C1_BLANK)) | C1x_UNDERSCORE },
        { U"h"sv,       C1x_HORIZONTAL },
        { U"l"sv,       C1_LOWER },
        { U"lower"sv,   C1_LOWER },
        { U"print"sv,   C1_ALL & ~C1_CNTRL },
        { U"punct"sv,   C1_PUNCT },
        { U"s"sv,       C1_SPACE },
        { U"space"sv,   C1_SPACE },
        { U"u"sv,       C1_UPPER },
        { U"unicode"sv, C1x_EXTENDED },
        { U"upper"sv,   C1_UPPER },
        { U"v"sv,       C1x_VERTICAL },
        { U"w"sv,       C1_ALPHA | C1_DIGIT | C1x_UNDERSCORE },
        { U"word"sv,    C1_ALPHA | C1_DIGIT | C1x_UNDERSCORE },
        { U"xdigit"sv,  C1_XDIGIT }
    };

    for (auto const& classFlag : classFlags)
    {
        if (EqualAsciiIgnoreCase(classFlag.name, className))
        {
            return classFlag.type;
        }
    }

    return 0;
}

WindowsChar32RegexTraits::string_type
WindowsChar32RegexTraits::lookup_collatename(_In_reads_to_ptr_(pchEnd) char_type const* pchBegin, char_type const* pchEnd) const
{
    static PCSTR const PosixCollateNames[] = {
        "NUL", "SOH", "STX", "ETX", "EOT", "ENQ", "ACK", "alert", "backspace", "tab", "newline",
        "vertical-tab", "form-feed", "carriage-return", "SO", "SI", "DLE", "DC1", "DC2", "DC3",
        "DC4", "NAK", "SYN", "ETB", "CAN", "EM", "SUB", "ESC", "IS4", "IS3", "IS2", "IS1", "space",
        "exclamation-mark", "quotation-mark", "number-sign", "dollar-sign", "percent-sign",
        "ampersand", "apostrophe", "left-parenthesis", "right-parenthesis", "asterisk",
        "plus-sign", "comma", "hyphen", "period", "slash", "zero", "one", "two", "three", "four",
        "five", "six", "seven", "eight", "nine", "colon", "semicolon", "less-than-sign",
        "equals-sign", "greater-than-sign", "question-mark",
        "commercial-at", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O",
        "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "left-square-bracket", "backslash",
        "right-square-bracket", "circumflex", "underscore",
        "grave-accent", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o",
        "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z", "left-curly-bracket",
        "vertical-line", "right-curly-bracket", "tilde", "DEL",
    };
    static unsigned const PosixCollateNamesCount = sizeof(PosixCollateNames) / sizeof(PosixCollateNames[0]);

    static PCSTR const DigraphCollateNames[] = {
      "ae",
      "Ae",
      "AE",
      "ch",
      "Ch",
      "CH",
      "ll",
      "Ll",
      "LL",
      "ss",
      "Ss",
      "SS",
      "nj",
      "Nj",
      "NJ",
      "dz",
      "Dz",
      "DZ",
      "lj",
      "Lj",
      "LJ",
    };
    static unsigned const DigraphCollateNamesCount = sizeof(DigraphCollateNames) / sizeof(DigraphCollateNames[0]);

    std::u32string_view collateName(pchBegin, pchEnd - pchBegin);

    // Posix collating names: If name == PosixCollateNames[N] then return "\N".
    for (unsigned i = 0; i != PosixCollateNamesCount; i += 1)
    {
        std::string_view posixName(PosixCollateNames[i]);
        if (EqualChar8Char32(posixName, collateName))
        {
            // Create a string containing the single character with code point i.
            return string_type(1, static_cast<char32_t>(i));
        }
    }

    // Digraph collating names: If name == "AB" then return "AB".
    for (unsigned i = 0; i != DigraphCollateNamesCount; i += 1)
    {
        std::string_view digraphName(DigraphCollateNames[i]);
        if (EqualChar8Char32(digraphName, collateName))
        {
            // Create a string containing the two characters in the digraph, cast from char to char32_t.
            return string_type(
                reinterpret_cast<unsigned char const*>(digraphName.data()),
                reinterpret_cast<unsigned char const*>(digraphName.data()) + digraphName.size());
        }
    }

    // Not a recognized collate name.
    return string_type();
}

bool
WindowsChar32RegexTraits::isctype(char_type ch, char_class_type ctype) const noexcept
{
    char_class_type type = ch < AsciiCharClassMax
        ? AsciiCharClass[ch]
        : GetCharClass(ch, ctype);
    return 0 != (ctype & type);
}

int
WindowsChar32RegexTraits::value(char_type ch, int radix) const noexcept
{
    int val;
    if (ch >= U'0' && ch <= U'9')
    {
        val = ch - U'0';
    }
    else if (
        unsigned const chLower = ch | 0x20;
        chLower >= U'a' && chLower <= U'z')
    {
        val = chLower - U'a' + 10;
    }
    else
    {
        return -1;
    }

    return val < radix ? val : -1;
}

WindowsChar32RegexTraits::locale_type
WindowsChar32RegexTraits::imbue(locale_type const loc)
{
    auto const old = m_lcid;

    if (loc == old)
    {
        return old;
    }

    for (auto latinCompatibleLcid : KnownLatinCompatibleLcids)
    {
        // If switching to a latin-compatible locale, use the hard-coded table.
        if (loc == latinCompatibleLcid)
        {
            // If table is already LOCALE_NEUTRAL then no need to re-initialize it.
            if (old != 0)
            {
                AsciiToLowerInitLatin();
            }

            m_lcid = loc;
            return old;
        }
    }

    AsciiToLowerInit(loc);
    m_lcid = loc;
    return old;
}

WindowsChar32RegexTraits::locale_type
WindowsChar32RegexTraits::getloc() const noexcept
{
    return m_lcid;
}

::boost::regex_constants::syntax_type
WindowsChar32RegexTraits::syntax_type(char_type ch) const noexcept
{
    return ch < AsciiTableMax
        ? ::boost::BOOST_REGEX_DETAIL_NS::get_default_syntax_type(static_cast<char>(ch))
        : ::boost::regex_constants::syntax_char;
}

::boost::regex_constants::escape_syntax_type
WindowsChar32RegexTraits::escape_syntax_type(char_type ch) const noexcept
{
    return ch < AsciiTableMax
        ? ::boost::BOOST_REGEX_DETAIL_NS::get_default_escape_syntax_type(static_cast<char>(ch))
        : ::boost::regex_constants::escape_type_identity;
}

WindowsChar32RegexTraits::char_type
WindowsChar32RegexTraits::translate(char_type ch, bool icase) const noexcept
{
    return icase
        ? translate_nocase(ch)
        : translate(ch);
}

std::intmax_t
WindowsChar32RegexTraits::toi(char_type const*& p1, char_type const* p2, int radix) const noexcept
{
    return ::boost::BOOST_REGEX_DETAIL_NS::global_toi(p1, p2, radix, *this);
}

_Ret_z_ char const*
WindowsChar32RegexTraits::error_string(::boost::regex_constants::error_type e) const noexcept
{
    return ::boost::BOOST_REGEX_DETAIL_NS::get_default_error_string(e);
}

WindowsChar32RegexTraits::char_type
WindowsChar32RegexTraits::tolower(char_type ch) const noexcept
{
    return ch < AsciiCharClassMax
        ? m_asciiToLower[ch]
        : ToUpperLowerImpl(ch, LCMAP_LOWERCASE | LCMAP_LINGUISTIC_CASING);
}

WindowsChar32RegexTraits::char_type
WindowsChar32RegexTraits::toupper(char_type ch) const noexcept
{
    // Note: tolower has an ASCII fast-path because tolower is used during case-insensitive matching
    // (translate_nocase. toupper is only used for case conversions, and it's a non-standard boost
    // regex extension, so it doesn't really need a fast-path optimization.
    return ToUpperLowerImpl(ch, LCMAP_UPPERCASE | LCMAP_LINGUISTIC_CASING);
}

WindowsChar32RegexTraits::char_class_type
WindowsChar32RegexTraits::GetCharClass(char32_t ch, char_class_type filter) noexcept
{
    char_class_type type = 0;

    if (CodePoint::IsBmp(ch))
    {
        char_class_type type1 = 0;
        wchar_t ch16 = static_cast<wchar_t>(ch);
        if (filter & (C1_ALL | C1x_HORIZONTAL | C1x_VERTICAL))
        {
            (void)GetStringTypeW(CT_CTYPE1, &ch16, 1, &type1);
        }

        char_class_type typeExtended = ch16 >= 0x100
            ? C1x_EXTENDED
            : 0;

        char_class_type typeSpecial;
        if (ch16 == L'_')
        {
            typeSpecial = C1x_UNDERSCORE;
        }
        else if (
            0 != (filter & (C1x_HORIZONTAL | C1x_VERTICAL)) &&
            0 != (type1 & C1_SPACE))
        {
            switch (ch16)
            {
            case L'\n':
            case L'\r':
            case L'\f':
            case L'\v':
            case L'\u2028': // LINE SEPARATOR
            case L'\u2029': // PARAGRAPH SEPARATOR
            case L'\u0085': // NEXT LINE
                typeSpecial = C1x_VERTICAL;
                break;
            default:
                // It's a space but not a vertical space. It must be a horizontal space.
                typeSpecial = C1x_HORIZONTAL;
                break;
            }
        }
        else
        {
            typeSpecial = 0;
        }

        type = type1 | typeExtended | typeSpecial;
    }
    else if (CodePoint::IsCodePoint(ch))
    {
        type = C1_DEFINED | C1x_EXTENDED;
    }

    return type;
}

// For testing.
bool
WindowsChar32RegexTraits::HardcodedTablesOk()
{
    for (WORD ch = 0; ch != AsciiCharClassMax; ch += 1)
    {
        auto const gcc = GetCharClass(ch, (char_class_type)(-1));
        auto const acc = AsciiCharClass[ch];
        if (gcc != acc)
        {
            return false;
        }
    }

    WindowsChar32RegexTraits t1, t2;

    // Default-initialized should match AsciiToLowerInitLatin.
    t2.AsciiToLowerInitLatin();
    for (wchar_t i = 0; i != AsciiTableMax; i += 1)
    {
        if (t1.m_asciiToLower[i] != t2.m_asciiToLower[i])
        {
            return false;
        }
    }

    // Default-initialized should match LCMapStringW for latin-compatible locales.
    for (unsigned short latinCompatibleLcid : KnownLatinCompatibleLcids)
    {
        t2.AsciiToLowerInit(latinCompatibleLcid);
        for (wchar_t i = 0; i != AsciiTableMax; i += 1)
        {
            if (t1.m_asciiToLower[i] != t2.m_asciiToLower[i])
            {
                return false;
            }
        }
    }

    return true;
}

WindowsChar32RegexTraits::string_type
WindowsChar32RegexTraits::TransformImpl(
    _In_reads_to_ptr_(pchEnd) char_type const* pchBegin,
    char_type const* pchEnd,
    unsigned mapFlags) const
{
    if (pchBegin == pchEnd)
    {
        // Empty string should sort first.
        return string_type();
    }

    std::u16string input16;
    std::u32string_view input32(pchBegin, pchEnd - pchBegin);
    input16.reserve(input32.size());
    for (char_type ch : input32)
    {
        char16_t utf16buffer[2];
        auto const cch = Utf16LE().Encode(utf16buffer, ch);
        input16.append(utf16buffer, cch);
    }

    if (input16.size() > INT_MAX)
    {
        throw std::runtime_error("Input string is too long for LCMapStringW");
    }

    auto const inputWide = reinterpret_cast<wchar_t const*>(input16.data());
    auto const neededLen = LCMapStringW(m_lcid, mapFlags, inputWide, static_cast<int>(input16.size()), nullptr, 0);
    if (neededLen > 0)
    {
        std::u32string sortKey;

        // LCMapString needs room for len bytes, then padding to a multiple of 4, then one more char32_t for a
        // suffix. The suffix is to help distinguish between cases where LCMapString returns { 1, 2 } versus
        // { 1, 2, 0 }. After rounding the length to a multiple of 4, this distinction would be lost, so we add
        // a suffix to preserve it. The suffix is the low 3 bits of the length returned by LCMapString.
        auto const sortKeyChars = (neededLen + sizeof(char32_t) - 1) / sizeof(char32_t);
        sortKey.resize(sortKeyChars + 1); // +1 for the suffix
        auto const actualLen = LCMapStringW(m_lcid, mapFlags, inputWide, static_cast<int>(input16.size()), reinterpret_cast<wchar_t*>(sortKey.data()), static_cast<int>(sortKey.size() * sizeof(char32_t)));
        if (actualLen != neededLen)
        {
            throw std::runtime_error("LCMapStringW returned an unexpected length");
        }

        // Set the suffix to the low 3 bits of neededLen, as described above.
        // Doesn't actually matter whether this gets byte-swapped.
        sortKey[sortKeyChars] = neededLen & (sizeof(char32_t) - 1);

        // Returned value is intended to be compared with memcmp, but u32string comparisons work on char32_t elements.
        // Win32 is little-endian, so fixing this up requires swapping the byte order of each char32_t in sortKey.
        for (char32_t& ch : sortKey)
        {
            ch = _byteswap_ulong(ch);
        }
        return sortKey;
    }

    throw std::runtime_error("LCMapStringW failed in TransformImpl with error " + std::to_string(GetLastError()));
}

WindowsChar32RegexTraits::char_type
WindowsChar32RegexTraits::ToUpperLowerImpl(char_type ch, unsigned mapFlags) const noexcept
{
    wchar_t src[2];
    wchar_t dst[2];

    if (CodePoint::IsBmp(ch))
    {
        src[0] = static_cast<wchar_t>(ch);
        auto const cch = LCMapStringW(m_lcid, mapFlags, src, 1, dst, 1);
        return cch == 1 ? dst[0] : ch;
    }
    else
    {
        Utf16LE().EncodeNonBmp(reinterpret_cast<char16_t*>(src), ch);
        auto const cch = LCMapStringW(m_lcid, mapFlags, src, 2, dst, 2);
        return cch == 2 ? CodePoint::FromSurrogatePair(dst[0], dst[1]) : ch;
    }
}

void
WindowsChar32RegexTraits::AsciiToLowerInit(locale_type loc)
{
    wchar_t src[AsciiTableMax];
    wchar_t dst[AsciiTableMax];
    for (wchar_t i = 0; i != AsciiTableMax; i += 1)
    {
        src[i] = i;
    }

    auto const cch = LCMapStringW(loc, LCMAP_LOWERCASE | LCMAP_LINGUISTIC_CASING, src, AsciiTableMax, dst, AsciiTableMax);
    if (cch != AsciiTableMax)
    {
        throw std::runtime_error("LCMapStringW failed in AsciiToLowerInit");
    }

    for (wchar_t i = 0; i != AsciiTableMax; i += 1)
    {
        m_asciiToLower[i] = dst[i];
    }

    m_lcid = loc;
}
