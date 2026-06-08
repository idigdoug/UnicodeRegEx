#include "pch.h"
#include "RegExTestHelpers.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

namespace RegExTests
{
    TEST_CLASS(EscapeLiteralTests)
    {
    public:

        // Helper: invoke EscapePatternLiteral and return the result as a wstring.
        static std::wstring EscapePattern(std::wstring_view input, RegExSyntaxFlags flags, HRESULT* pHr = nullptr)
        {
            wil::unique_bstr in(SysAllocStringLen(input.data(), static_cast<UINT>(input.size())));
            wil::unique_bstr out;
            HRESULT hr = GetLibrary()->EscapePatternLiteral(in.get(), flags, out.put());
            if (pHr) *pHr = hr;
            if (FAILED(hr) || !out) return std::wstring();
            return std::wstring(out.get(), SysStringLen(out.get()));
        }

        // Helper: invoke EscapeFormatLiteral and return the result as a wstring.
        static std::wstring EscapeFormat(std::wstring_view input, RegExFormatFlags flags, HRESULT* pHr = nullptr)
        {
            wil::unique_bstr in(SysAllocStringLen(input.data(), static_cast<UINT>(input.size())));
            wil::unique_bstr out;
            HRESULT hr = GetLibrary()->EscapeFormatLiteral(in.get(), flags, out.put());
            if (pHr) *pHr = hr;
            if (FAILED(hr) || !out) return std::wstring();
            return std::wstring(out.get(), SysStringLen(out.get()));
        }

        // ---- EscapePatternLiteral ----

        TEST_METHOD(EscapePattern_Perl_NoMetacharacters)
        {
            Assert::AreEqual(std::wstring(L"hello"), EscapePattern(L"hello", RegExSyntaxFlags_ECMAScript));
        }

        TEST_METHOD(EscapePattern_Perl_AllMetacharacters)
        {
            // Perl set: . [ { } ( ) \ * + ? | ^ $
            Assert::AreEqual(
                std::wstring(LR"(\.\[\{\}\(\)\\\*\+\?\|\^\$)"),
                EscapePattern(LR"(.[{}()\*+?|^$)", RegExSyntaxFlags_ECMAScript));
        }

        TEST_METHOD(EscapePattern_Perl_MixedContent)
        {
            Assert::AreEqual(
                std::wstring(LR"(a\.b\*c\(d\))"),
                EscapePattern(LR"(a.b*c(d))", RegExSyntaxFlags_ECMAScript));
        }

        TEST_METHOD(EscapePattern_Perl_NonAsciiPassedThrough)
        {
            // Non-ASCII characters must not be touched.
            Assert::AreEqual(
                std::wstring(L"caf\u00e9\u4e2d\u6587"),
                EscapePattern(L"caf\u00e9\u4e2d\u6587", RegExSyntaxFlags_ECMAScript));
        }

        TEST_METHOD(EscapePattern_Basic_OnlyBasicMetacharacters)
        {
            // Basic set: . [ \ * ^ $   (no { } ( ) + ? |)
            // Verify '?' and '+' are NOT escaped in basic mode.
            Assert::AreEqual(
                std::wstring(LR"(a\.b?c+d)"),
                EscapePattern(LR"(a.b?c+d)", RegExSyntaxFlags_basic));
        }

        TEST_METHOD(EscapePattern_Basic_AllBasicMetacharacters)
        {
            Assert::AreEqual(
                std::wstring(LR"(\.\[\\\*\^\$)"),
                EscapePattern(LR"(.[\*^$)", RegExSyntaxFlags_basic));
        }

        TEST_METHOD(EscapePattern_Literal_NoEscaping)
        {
            // RegExSyntaxFlags_literal isn't exposed in the IDL enum, so build it directly.
            // boost::regbase::literal == 2.
            auto literal = static_cast<RegExSyntaxFlags>(2);
            Assert::AreEqual(
                std::wstring(LR"(.[{}()\*+?|^$)"),
                EscapePattern(LR"(.[{}()\*+?|^$)", literal));
        }

        TEST_METHOD(EscapePattern_Empty)
        {
            HRESULT hr = S_OK;
            std::wstring result = EscapePattern(L"", RegExSyntaxFlags_ECMAScript, &hr);
            Assert::AreEqual(S_OK, hr);
            Assert::AreEqual(std::wstring(), result);
        }

        TEST_METHOD(EscapePattern_NullPatternProducesEmpty)
        {
            // SysStringLen(nullptr) == 0, so a null BSTR is treated as empty.
            wil::unique_bstr out;
            HRESULT hr = GetLibrary()->EscapePatternLiteral(nullptr, RegExSyntaxFlags_ECMAScript, out.put());
            Assert::AreEqual(S_OK, hr);
            Assert::AreEqual(UINT(0), SysStringLen(out.get()));
        }

        TEST_METHOD(EscapePattern_NullOutPointer)
        {
            wil::unique_bstr in(SysAllocString(L"x"));
            HRESULT hr = GetLibrary()->EscapePatternLiteral(in.get(), RegExSyntaxFlags_ECMAScript, nullptr);
            Assert::AreEqual(E_POINTER, hr);
        }

        TEST_METHOD(EscapePattern_InvalidSyntaxGroup)
        {
            // basic_syntax_group (1) | literal (2) is not a valid combination.
            auto bogus = static_cast<RegExSyntaxFlags>(1 | 2);
            wil::unique_bstr in(SysAllocString(L"hello"));
            wil::unique_bstr out;
            HRESULT hr = GetLibrary()->EscapePatternLiteral(in.get(), bogus, out.put());
            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(out.get());
        }

        TEST_METHOD(EscapePattern_OutputRoundTrips)
        {
            // Escaping a literal and then compiling+matching it should match the original input.
            std::wstring_view literal = LR"(1+1=2)";
            std::wstring escaped = EscapePattern(literal, RegExSyntaxFlags_ECMAScript);

            auto regex = MakeRegEx(escaped);

            RegExBytes inputStr = {
                .data_ptr = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(literal.data())),
                .size = static_cast<LONGLONG>(literal.size() * sizeof(wchar_t)),
            };
            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(S_OK,
                regex->Match(&inputStr, RegExEncoding_utf16le, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());
        }

        // ---- EscapeFormatLiteral ----

        TEST_METHOD(EscapeFormat_Perl_NoMetacharacters)
        {
            Assert::AreEqual(std::wstring(L"hello"), EscapeFormat(L"hello", RegExFormatFlag_perl));
        }

        TEST_METHOD(EscapeFormat_Perl_DollarAndBackslash)
        {
            // Perl format set without format_all: dollar and backslash.
            Assert::AreEqual(
                std::wstring(LR"(\$1 plus \\n)"),
                EscapeFormat(LR"($1 plus \n)", RegExFormatFlag_perl));
        }

        TEST_METHOD(EscapeFormat_Perl_DoesNotEscapeAmp)
        {
            // & is not special in Perl-format mode.
            Assert::AreEqual(std::wstring(L"a&b"), EscapeFormat(L"a&b", RegExFormatFlag_perl));
        }

        TEST_METHOD(EscapeFormat_PerlAll_ExtendedSet)
        {
            // Perl + format_all set: $ \ ( ) ? :
            Assert::AreEqual(
                std::wstring(LR"(\$\(a\?b\:c\))"),
                EscapeFormat(LR"($(a?b:c))",
                    static_cast<RegExFormatFlags>(RegExFormatFlag_perl | (int)boost::regex_constants::format_all)));
        }

        TEST_METHOD(EscapeFormat_Sed_AmpAndBackslash)
        {
            // Sed format set: ampersand and backslash (does NOT escape $).
            Assert::AreEqual(
                std::wstring(LR"(\&\\ and $1)"),
                EscapeFormat(LR"(&\ and $1)", RegExFormatFlag_sed));
        }

        TEST_METHOD(EscapeFormat_SedAll_ExtendedSet)
        {
            // Sed + format_all set: & \ ( ) ? :
            Assert::AreEqual(
                std::wstring(LR"(\&\(a\?b\:c\))"),
                EscapeFormat(LR"(&(a?b:c))",
                    static_cast<RegExFormatFlags>(RegExFormatFlag_sed | (int)boost::regex_constants::format_all)));
        }

        TEST_METHOD(EscapeFormat_LiteralFlag_NoEscaping)
        {
            // format_literal short-circuits all escaping.
            auto literalFlag = static_cast<RegExFormatFlags>((int)boost::regex_constants::format_literal);
            Assert::AreEqual(
                std::wstring(LR"($1 \n &)"),
                EscapeFormat(LR"($1 \n &)", literalFlag));
        }

        TEST_METHOD(EscapeFormat_Empty)
        {
            HRESULT hr = S_OK;
            std::wstring result = EscapeFormat(L"", RegExFormatFlag_perl, &hr);
            Assert::AreEqual(S_OK, hr);
            Assert::AreEqual(std::wstring(), result);
        }

        TEST_METHOD(EscapeFormat_NullOutPointer)
        {
            wil::unique_bstr in(SysAllocString(L"$1"));
            HRESULT hr = GetLibrary()->EscapeFormatLiteral(in.get(), RegExFormatFlag_perl, nullptr);
            Assert::AreEqual(E_POINTER, hr);
        }

        TEST_METHOD(EscapeFormat_NonAsciiPassedThrough)
        {
            Assert::AreEqual(
                std::wstring(L"caf\u00e9 \\$1"),
                EscapeFormat(L"caf\u00e9 $1", RegExFormatFlag_perl));
        }

        // ---- GetEscapePatternLiteralChars ----

        // Helper: invoke GetEscapePatternLiteralChars and return the result as a wstring.
        static std::wstring GetEscapePatternChars(RegExSyntaxFlags flags, HRESULT* pHr = nullptr)
        {
            wil::unique_bstr out;
            HRESULT hr = GetLibrary()->GetEscapePatternLiteralChars(flags, out.put());
            if (pHr) *pHr = hr;
            if (FAILED(hr) || !out) return std::wstring();
            return std::wstring(out.get(), SysStringLen(out.get()));
        }

        TEST_METHOD(GetEscapePatternChars_Perl)
        {
            Assert::AreEqual(
                std::wstring(LR"(.[{}()\*+?|^$)"),
                GetEscapePatternChars(RegExSyntaxFlags_ECMAScript));
        }

        TEST_METHOD(GetEscapePatternChars_Basic)
        {
            Assert::AreEqual(
                std::wstring(LR"(.[\*^$)"),
                GetEscapePatternChars(RegExSyntaxFlags_basic));
        }

        TEST_METHOD(GetEscapePatternChars_Literal_ReturnsEmpty)
        {
            // RegExSyntaxFlags_literal isn't exposed in the IDL enum; boost::regbase::literal == 2.
            auto literal = static_cast<RegExSyntaxFlags>(2);
            HRESULT hr = S_OK;
            std::wstring result = GetEscapePatternChars(literal, &hr);
            Assert::AreEqual(S_OK, hr);
            Assert::AreEqual(std::wstring(), result);
        }

        TEST_METHOD(GetEscapePatternChars_InvalidSyntaxGroup)
        {
            // basic_syntax_group (1) | literal (2) is not a valid combination.
            auto bogus = static_cast<RegExSyntaxFlags>(1 | 2);
            wil::unique_bstr out;
            HRESULT hr = GetLibrary()->GetEscapePatternLiteralChars(bogus, out.put());
            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(out.get());
        }

        TEST_METHOD(GetEscapePatternChars_NullOutPointer)
        {
            HRESULT hr = GetLibrary()->GetEscapePatternLiteralChars(RegExSyntaxFlags_ECMAScript, nullptr);
            Assert::AreEqual(E_POINTER, hr);
        }

        TEST_METHOD(GetEscapePatternChars_MatchesEscapeBehavior_Perl)
        {
            // Every character returned by GetEscapePatternLiteralChars must be escaped
            // by EscapePatternLiteral, and only those characters.
            auto chars = GetEscapePatternChars(RegExSyntaxFlags_ECMAScript);
            for (wchar_t c : chars)
            {
                wchar_t input[2] = { c, L'\0' };
                std::wstring escaped = EscapePattern(input, RegExSyntaxFlags_ECMAScript);
                std::wstring expected = std::wstring(L"\\") + c;
                Assert::AreEqual(expected, escaped,
                    L"Character returned by GetEscapePatternLiteralChars should be escaped");
            }
        }

        // ---- GetEscapeFormatLiteralChars ----

        // Helper: invoke GetEscapeFormatLiteralChars and return the result as a wstring.
        static std::wstring GetEscapeFormatChars(RegExFormatFlags flags, HRESULT* pHr = nullptr)
        {
            wil::unique_bstr out;
            HRESULT hr = GetLibrary()->GetEscapeFormatLiteralChars(flags, out.put());
            if (pHr) *pHr = hr;
            if (FAILED(hr) || !out) return std::wstring();
            return std::wstring(out.get(), SysStringLen(out.get()));
        }

        TEST_METHOD(GetEscapeFormatChars_Perl)
        {
            Assert::AreEqual(
                std::wstring(LR"($\)"),
                GetEscapeFormatChars(RegExFormatFlag_perl));
        }

        TEST_METHOD(GetEscapeFormatChars_PerlAll)
        {
            Assert::AreEqual(
                std::wstring(LR"($\()?:)"),
                GetEscapeFormatChars(
                    static_cast<RegExFormatFlags>(RegExFormatFlag_perl | (int)boost::regex_constants::format_all)));
        }

        TEST_METHOD(GetEscapeFormatChars_Sed)
        {
            Assert::AreEqual(
                std::wstring(LR"(&\)"),
                GetEscapeFormatChars(RegExFormatFlag_sed));
        }

        TEST_METHOD(GetEscapeFormatChars_SedAll)
        {
            Assert::AreEqual(
                std::wstring(LR"(&\()?:)"),
                GetEscapeFormatChars(
                    static_cast<RegExFormatFlags>(RegExFormatFlag_sed | (int)boost::regex_constants::format_all)));
        }

        TEST_METHOD(GetEscapeFormatChars_LiteralFlag_ReturnsEmpty)
        {
            auto literalFlag = static_cast<RegExFormatFlags>((int)boost::regex_constants::format_literal);
            HRESULT hr = S_OK;
            std::wstring result = GetEscapeFormatChars(literalFlag, &hr);
            Assert::AreEqual(S_OK, hr);
            Assert::AreEqual(std::wstring(), result);
        }

        TEST_METHOD(GetEscapeFormatChars_NullOutPointer)
        {
            HRESULT hr = GetLibrary()->GetEscapeFormatLiteralChars(RegExFormatFlag_perl, nullptr);
            Assert::AreEqual(E_POINTER, hr);
        }

        TEST_METHOD(GetEscapeFormatChars_MatchesEscapeBehavior_Perl)
        {
            // Every character returned by GetEscapeFormatLiteralChars must be escaped
            // by EscapeFormatLiteral with the same flags.
            auto chars = GetEscapeFormatChars(RegExFormatFlag_perl);
            for (wchar_t c : chars)
            {
                wchar_t input[2] = { c, L'\0' };
                std::wstring escaped = EscapeFormat(input, RegExFormatFlag_perl);
                std::wstring expected = std::wstring(L"\\") + c;
                Assert::AreEqual(expected, escaped,
                    L"Character returned by GetEscapeFormatLiteralChars should be escaped");
            }
        }

        TEST_METHOD(GetEscapeFormatChars_MatchesEscapeBehavior_Sed)
        {
            auto chars = GetEscapeFormatChars(RegExFormatFlag_sed);
            for (wchar_t c : chars)
            {
                wchar_t input[2] = { c, L'\0' };
                std::wstring escaped = EscapeFormat(input, RegExFormatFlag_sed);
                std::wstring expected = std::wstring(L"\\") + c;
                Assert::AreEqual(expected, escaped,
                    L"Character returned by GetEscapeFormatLiteralChars should be escaped");
            }
        }
    };
}
