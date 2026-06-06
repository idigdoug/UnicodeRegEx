#include "pch.h"
#include "RegExTestHelpers.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

namespace RegExTests
{
    TEST_CLASS(SearchAndMatchTests)
    {
    public:

        TEST_METHOD(Search_FindsFirstMatch)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExString inputStr = MakeString(u8"abc 123 def 456"sv);

            wil::com_ptr<IRegExMatchResults> results;
            HRESULT hr = regex->Search(&inputStr, 0, RegExMatchFlag_default, results.put());
            Assert::AreEqual(S_OK, hr);
            Assert::IsNotNull(results.get());

            // Search returns only the first match.
            RegExSubMatch sub = {};
            Assert::AreEqual(S_OK, results->GetSubMatch(0, &sub));
            Assert::AreEqual(VARIANT_TRUE, sub.matched);
            Assert::AreEqual(LONGLONG(4), sub.input_offset);
            Assert::AreEqual(LONGLONG(3), sub.size);
        }

        TEST_METHOD(Search_NoMatch_ReturnsNull)
        {
            auto regex = MakeRegEx(L"xyz");

            RegExString inputStr = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            HRESULT hr = regex->Search(&inputStr, 0, RegExMatchFlag_default, results.put());
            Assert::AreEqual(S_OK, hr);
            Assert::IsNull(results.get());
        }

        TEST_METHOD(Search_StartByteOffset_SkipsEarlier)
        {
            // With startByteOffset past the first match, Search should find the second.
            auto regex = MakeRegEx(L"\\d+");

            RegExString inputStr = MakeString(u8"abc 123 def 456"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputStr, 8, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(12), sub.input_offset);
            Assert::AreEqual(LONGLONG(3), sub.size);
        }

        TEST_METHOD(Search_CaptureGroups)
        {
            auto regex = MakeRegEx(L"(\\w+)@(\\w+)");

            RegExString inputStr = MakeString(u8"$user@host!"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputStr, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            UINT32 count = 0;
            results->get_SubMatchCount(&count);
            Assert::AreEqual(UINT32(3), count);

            RegExSubMatch sub = {};
            results->GetSubMatch(1, &sub);
            Assert::AreEqual(LONGLONG(1), sub.input_offset);
            Assert::AreEqual(LONGLONG(4), sub.size);

            results->GetSubMatch(2, &sub);
            Assert::AreEqual(LONGLONG(6), sub.input_offset);
            Assert::AreEqual(LONGLONG(4), sub.size);
        }

        TEST_METHOD(Search_Format)
        {
            auto regex = MakeRegEx(L"(\\w+) (\\w+)");

            RegExString inputStr = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputStr, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            wil::unique_bstr formatTemplate(SysAllocString(L"$2 $1"));
            Assert::AreEqual(
                S_OK,
                results->SetFormatTemplate(formatTemplate.get(), RegExFormatFlag_default));

            RegExString output = {};
            Assert::AreEqual(
                S_OK,
                results->Format(RegExEncoding_utf8, &output));
            Assert::IsTrue(RegExEncoding_utf8 == output.encoding);
            Assert::AreEqual(std::string_view("world hello"), MakeView<char>(output));
        }

        TEST_METHOD(Search_DoesNotExposeIRegExMatchEnumerator)
        {
            auto regex = MakeRegEx(L"x");

            RegExString inputStr = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputStr, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = results->QueryInterface(__uuidof(IRegExMatchEnumerator), enumerator.put_void());
            Assert::AreEqual(E_NOINTERFACE, hr);
            Assert::IsNull(enumerator.get());
        }

        TEST_METHOD(Search_RejectsMatchPrevAvail)
        {
            auto regex = MakeRegEx(L"x");

            RegExString inputStr = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchResults> results;
            HRESULT hr = regex->Search(
                &inputStr,
                0,
                static_cast<RegExMatchFlags>(boost::regex_constants::match_prev_avail),
                results.put());
            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(results.get());
        }

        TEST_METHOD(Search_StartOffsetPastEnd_Fails)
        {
            auto regex = MakeRegEx(L"x");

            RegExString inputStr = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            HRESULT hr = regex->Search(&inputStr, 999, RegExMatchFlag_default, results.put());
            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(results.get());
        }

        TEST_METHOD(Match_WholeStringMatches)
        {
            // regex_match requires the pattern to consume the entire input.
            auto regex = MakeRegEx(L"\\d+");

            RegExString inputStr = MakeString(u8"12345"sv);

            wil::com_ptr<IRegExMatchResults> results;
            HRESULT hr = regex->Match(&inputStr, 0, RegExMatchFlag_default, results.put());
            Assert::AreEqual(S_OK, hr);
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(0), sub.input_offset);
            Assert::AreEqual(LONGLONG(5), sub.size);
        }

        TEST_METHOD(Match_PartialMatch_ReturnsNull)
        {
            // Pattern matches only the start of the input, so Match should fail even
            // though Search would succeed.
            auto regex = MakeRegEx(L"\\d+");

            RegExString inputStr = MakeString(u8"123 abc"sv);

            wil::com_ptr<IRegExMatchResults> matchResults;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputStr, 0, RegExMatchFlag_default, matchResults.put()));
            Assert::IsNull(matchResults.get());

            // Sanity check: Search does succeed on the same input.
            wil::com_ptr<IRegExMatchResults> searchResults;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputStr, 0, RegExMatchFlag_default, searchResults.put()));
            Assert::IsNotNull(searchResults.get());
        }

        TEST_METHOD(Match_CaptureGroups)
        {
            auto regex = MakeRegEx(L"(\\w+)=(\\w+)");

            RegExString inputStr = MakeString(u8"key=value"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputStr, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            UINT32 count = 0;
            results->get_SubMatchCount(&count);
            Assert::AreEqual(UINT32(3), count);

            RegExString str = {};
            Assert::AreEqual(
                S_OK,
                results->GetSubMatchString(1, RegExEncoding_utf8, &str));
            Assert::AreEqual(std::string_view("key"), MakeView<char>(str));

            Assert::AreEqual(
                S_OK,
                results->GetSubMatchString(2, RegExEncoding_utf8, &str));
            Assert::AreEqual(std::string_view("value"), MakeView<char>(str));
        }

        TEST_METHOD(Match_NoMatch_ReturnsNull)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExString inputStr = MakeString(u8"abc"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputStr, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNull(results.get());
        }

        TEST_METHOD(Match_StartByteOffset)
        {
            // With startByteOffset = 4, regex_match should match only the suffix "def".
            auto regex = MakeRegEx(L"\\w+");

            RegExString inputStr = MakeString(u8"abc def"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputStr, 4, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(4), sub.input_offset);
            Assert::AreEqual(LONGLONG(3), sub.size);
        }
    };
}
