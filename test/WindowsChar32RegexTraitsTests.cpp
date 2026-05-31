#include "pch.h"
#include <WindowsChar32RegexTraits.h>
#include <utf.h>

#include <regex>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

namespace WindowsChar32RegexTraitsTests
{
    TEST_CLASS(TraitsBasicTests)
    {
    public:

        TEST_METHOD(HardcodedTablesOk)
        {
            Assert::IsTrue(WindowsChar32RegexTraits::HardcodedTablesOk());
        }

        TEST_METHOD(TranslateNoCase_AsciiUpperToLower)
        {
            WindowsChar32RegexTraits traits;
            Assert::AreEqual(char32_t('a'), traits.translate_nocase('A'));
            Assert::AreEqual(char32_t('z'), traits.translate_nocase('Z'));
            Assert::AreEqual(char32_t('a'), traits.translate_nocase('a'));
        }

        TEST_METHOD(TranslateNoCase_NonAscii)
        {
            WindowsChar32RegexTraits traits;
            // U+00C9 (E-acute uppercase) should map to U+00E9 (e-acute lowercase)
            char32_t result = traits.translate_nocase(0xC9);
            Assert::AreEqual(char32_t(0xE9), result);
        }

        TEST_METHOD(Translate_Identity)
        {
            WindowsChar32RegexTraits traits;
            Assert::AreEqual(char32_t('A'), traits.translate('A'));
            Assert::AreEqual(char32_t(0x20AC), traits.translate(0x20AC));
        }

        TEST_METHOD(LookupClassname_Alpha)
        {
            WindowsChar32RegexTraits traits;
            const char32_t name[] = U"alpha";
            auto cls = traits.lookup_classname(name, name + 5);
            Assert::IsTrue(cls != 0);
            Assert::IsTrue(traits.isctype('A', cls));
            Assert::IsTrue(traits.isctype('z', cls));
            Assert::IsFalse(traits.isctype('0', cls));
        }

        TEST_METHOD(LookupClassname_Digit)
        {
            WindowsChar32RegexTraits traits;
            const char32_t name[] = U"digit";
            auto cls = traits.lookup_classname(name, name + 5);
            Assert::IsTrue(cls != 0);
            Assert::IsTrue(traits.isctype('0', cls));
            Assert::IsTrue(traits.isctype('9', cls));
            Assert::IsFalse(traits.isctype('A', cls));
        }

        TEST_METHOD(LookupClassname_Space)
        {
            WindowsChar32RegexTraits traits;
            const char32_t name[] = U"space";
            auto cls = traits.lookup_classname(name, name + 5);
            Assert::IsTrue(cls != 0);
            Assert::IsTrue(traits.isctype(' ', cls));
            Assert::IsTrue(traits.isctype('\t', cls));
            Assert::IsFalse(traits.isctype('A', cls));
        }

        TEST_METHOD(Value_Decimal)
        {
            WindowsChar32RegexTraits traits;
            Assert::AreEqual(0, traits.value('0', 10));
            Assert::AreEqual(9, traits.value('9', 10));
            Assert::AreEqual(-1, traits.value('A', 10));
        }

        TEST_METHOD(Value_Hex)
        {
            WindowsChar32RegexTraits traits;
            Assert::AreEqual(10, traits.value('a', 16));
            Assert::AreEqual(15, traits.value('F', 16));
            Assert::AreEqual(0, traits.value('0', 16));
        }

        TEST_METHOD(ToLower)
        {
            WindowsChar32RegexTraits traits;
            Assert::AreEqual(char32_t('a'), traits.tolower('A'));
            Assert::AreEqual(char32_t('z'), traits.tolower('Z'));
            Assert::AreEqual(char32_t('a'), traits.tolower('a'));
        }

        TEST_METHOD(ToUpper)
        {
            WindowsChar32RegexTraits traits;
            Assert::AreEqual(char32_t('A'), traits.toupper('a'));
            Assert::AreEqual(char32_t('Z'), traits.toupper('z'));
            Assert::AreEqual(char32_t('A'), traits.toupper('A'));
        }

        TEST_METHOD(ErrorString)
        {
            WindowsChar32RegexTraits traits;
            auto msg = traits.error_string(boost::regex_constants::error_ok);
            Assert::IsNotNull(msg);
        }
    };

    TEST_CLASS(BoostRegexTests)
    {
    public:

        TEST_METHOD(SimpleMatch)
        {
            boost::basic_regex<char32_t, WindowsChar32RegexTraits> re(U"hello");
            std::u32string input = U"say hello world";

            auto [begin, end] = std::make_pair(input.begin(), input.end());
            boost::match_results<std::u32string::const_iterator> results;
            Assert::IsTrue(boost::regex_search(input.cbegin(), input.cend(), results, re));
            Assert::AreEqual(size_t(4), (size_t)results.position(size_t(0)));
        }

        TEST_METHOD(CaseInsensitive)
        {
            boost::basic_regex<char32_t, WindowsChar32RegexTraits> re(
                U"hello", boost::regex_constants::icase);
            std::u32string input = U"say HELLO world";
            boost::match_results<std::u32string::const_iterator> results;
            Assert::IsTrue(boost::regex_search(input.cbegin(), input.cend(), results, re));
        }

        TEST_METHOD(CaptureGroups)
        {
            boost::basic_regex<char32_t, WindowsChar32RegexTraits> re(U"(\\w+)@(\\w+)");
            std::u32string input = U"user@host";
            boost::match_results<std::u32string::const_iterator> results;
            Assert::IsTrue(boost::regex_search(input.cbegin(), input.cend(), results, re));
            Assert::AreEqual(size_t(3), results.size()); // full match + 2 groups
            std::u32string g1(results[1].first, results[1].second);
            std::u32string g2(results[2].first, results[2].second);
            Assert::IsTrue(g1 == U"user");
            Assert::IsTrue(g2 == U"host");
        }

        TEST_METHOD(UnicodeMatch)
        {
            // Match a 4-byte code point.
            boost::basic_regex<char32_t, WindowsChar32RegexTraits> re(U"\x1F600");
            std::u32string input = U"smile \x1F600 here";
            boost::match_results<std::u32string::const_iterator> results;
            Assert::IsTrue(boost::regex_search(input.cbegin(), input.cend(), results, re));
        }

        TEST_METHOD(WithUtf8Iterator)
        {
            // Use the regex over UTF-8 data via Char32Utf8_iterator.
            boost::basic_regex<char32_t, WindowsChar32RegexTraits> re(U"world");
            auto const data = u8"hello world"sv;
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);

            boost::match_results<utf8::CodePointIterator> results;
            Assert::IsTrue(boost::regex_search(begin, end, results, re));
            // "world" starts at byte offset 6.
            Assert::AreEqual(size_t(6), results[0].first.ByteOffset(data.data()));
        }

        TEST_METHOD(WithUtf16LEIterator)
        {
            boost::basic_regex<char32_t, WindowsChar32RegexTraits> re(U"world");
            auto const data = u"hello world"sv;
            auto [begin, end] = utf16le::CodePointIterator::FromSpan(data);

            boost::match_results<utf16le::CodePointIterator> results;
            Assert::IsTrue(boost::regex_search(begin, end, results, re));
            Assert::AreEqual(size_t(12), results[0].first.ByteOffset(data.data())); // 6 chars * 2 bytes
        }

        TEST_METHOD(NoMatch)
        {
            boost::basic_regex<char32_t, WindowsChar32RegexTraits> re(U"xyz");
            std::u32string input = U"hello world";
            boost::match_results<std::u32string::const_iterator> results;
            Assert::IsFalse(boost::regex_search(input.cbegin(), input.cend(), results, re));
        }

        TEST_METHOD(FormatReplacement)
        {
            boost::basic_regex<char32_t, WindowsChar32RegexTraits> re(U"(\\w+)@(\\w+)");
            std::u32string input = U"user@host";
            boost::match_results<std::u32string::const_iterator> results;
            Assert::IsTrue(boost::regex_search(input.cbegin(), input.cend(), results, re));

            std::u32string replacement = U"$2@$1";
            std::u32string output;
            results.format(std::back_inserter(output), replacement);
            Assert::IsTrue(output == U"host@user");
        }
    };

    TEST_CLASS(StdRegexTests)
    {
    public:

        TEST_METHOD(SimpleMatch)
        {
            std::basic_regex<char32_t, WindowsChar32RegexTraits> re(U"hello");
            std::u32string input = U"say hello world";
            std::match_results<std::u32string::const_iterator> results;
            Assert::IsTrue(std::regex_search(input.cbegin(), input.cend(), results, re));
            Assert::AreEqual(size_t(4), (size_t)results.position(size_t(0)));
        }

        TEST_METHOD(CaseInsensitive)
        {
            std::basic_regex<char32_t, WindowsChar32RegexTraits> re(
                U"hello", std::regex_constants::icase);
            std::u32string input = U"say HELLO world";
            std::match_results<std::u32string::const_iterator> results;
            Assert::IsTrue(std::regex_search(input.cbegin(), input.cend(), results, re));
        }

        TEST_METHOD(CaptureGroups)
        {
            std::basic_regex<char32_t, WindowsChar32RegexTraits> re(U"(\\w+)@(\\w+)");
            std::u32string input = U"user@host";
            std::match_results<std::u32string::const_iterator> results;
            Assert::IsTrue(std::regex_search(input.cbegin(), input.cend(), results, re));
            Assert::AreEqual(size_t(3), results.size());
            std::u32string g1(results[1].first, results[1].second);
            Assert::IsTrue(g1 == U"user");
        }
    };
}
