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

            RegExBytes inputBytes = MakeString(u8"abc 123 def 456"sv);

            wil::com_ptr<IRegExMatchResults> results;
            HRESULT hr = regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
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

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            HRESULT hr = regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
            Assert::AreEqual(S_OK, hr);
            Assert::IsNull(results.get());
        }

        TEST_METHOD(Search_StartByteOffset_SkipsEarlier)
        {
            // With startByteOffset past the first match, Search should find the second.
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"abc 123 def 456"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputBytes, RegExEncoding_utf8, 8, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(12), sub.input_offset);
            Assert::AreEqual(LONGLONG(3), sub.size);
        }

        TEST_METHOD(Search_CaptureGroups)
        {
            auto regex = MakeRegEx(L"(\\w+)@(\\w+)");

            RegExBytes inputBytes = MakeString(u8"$user@host!"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
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

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            wil::unique_bstr formatTemplate(SysAllocString(L"$2 $1"));
            Assert::AreEqual(
                S_OK,
                results->SetFormatTemplate(formatTemplate.get(), RegExFormatFlag_default));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                results->Format(output.put()));
            Assert::AreEqual(L"world hello"sv, MakeView(output.get()));
        }

        TEST_METHOD(Search_DoesNotExposeIRegExMatchEnumerator)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = results->QueryInterface(__uuidof(IRegExMatchEnumerator), enumerator.put_void());
            Assert::AreEqual(E_NOINTERFACE, hr);
            Assert::IsNull(enumerator.get());
        }

        TEST_METHOD(Search_RejectsMatchPrevAvail)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchResults> results;
            HRESULT hr = regex->Search(
                &inputBytes,
                RegExEncoding_utf8,
                0,
                static_cast<RegExMatchFlags>(boost::regex_constants::match_prev_avail),
                results.put());
            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(results.get());
        }

        TEST_METHOD(Search_StartOffsetPastEnd_Fails)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            HRESULT hr = regex->Search(&inputBytes, RegExEncoding_utf8, 999, RegExMatchFlag_default, results.put());
            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(results.get());
        }

        TEST_METHOD(Match_WholeStringMatches)
        {
            // regex_match requires the pattern to consume the entire input.
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"12345"sv);

            wil::com_ptr<IRegExMatchResults> results;
            HRESULT hr = regex->Match(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
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

            RegExBytes inputBytes = MakeString(u8"123 abc"sv);

            wil::com_ptr<IRegExMatchResults> matchResults;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, matchResults.put()));
            Assert::IsNull(matchResults.get());

            // Sanity check: Search does succeed on the same input.
            wil::com_ptr<IRegExMatchResults> searchResults;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, searchResults.put()));
            Assert::IsNotNull(searchResults.get());
        }

        TEST_METHOD(Match_CaptureGroups)
        {
            auto regex = MakeRegEx(L"(\\w+)=(\\w+)");

            RegExBytes inputBytes = MakeString(u8"key=value"sv);
            RegExSubMatch sub = {};

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            UINT32 count = 0;
            results->get_SubMatchCount(&count);
            Assert::AreEqual(UINT32(3), count);

            RegExBytes str = {};
            Assert::AreEqual(
                S_OK,
                results->GetSubMatch(1, &sub));
            Assert::AreEqual(LONGLONG(0), sub.input_offset);
            Assert::AreEqual(LONGLONG(3), sub.size);
            Assert::AreEqual(VARIANT_TRUE, sub.matched);

            Assert::AreEqual(
                S_OK,
                results->GetSubMatch(2, &sub));
            Assert::AreEqual(LONGLONG(4), sub.input_offset);
            Assert::AreEqual(LONGLONG(5), sub.size);
            Assert::AreEqual(VARIANT_TRUE, sub.matched);
        }

        TEST_METHOD(Match_NoMatch_ReturnsNull)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"abc"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNull(results.get());
        }

        TEST_METHOD(Match_StartByteOffset)
        {
            // With startByteOffset = 4, regex_match should match only the suffix "def".
            auto regex = MakeRegEx(L"\\w+");

            RegExBytes inputBytes = MakeString(u8"abc def"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputBytes, RegExEncoding_utf8, 4, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(4), sub.input_offset);
            Assert::AreEqual(LONGLONG(3), sub.size);
        }
    };
}
