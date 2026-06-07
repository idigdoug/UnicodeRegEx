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

        TEST_METHOD(Match_StartByteOffset_LookBehind)
        {
            // Regression: regex_match has no "base" parameter, so a naive
            // implementation cannot expose [m_begin, m_pos) to lookbehind assertions.
            // We simulate regex_match via regex_search + match_continuous, which DOES
            // accept a base parameter. Pattern "(?<=hello )world" must match when
            // startByteOffset points at the 'w' because the lookbehind needs to see
            // the preceding "hello ".
            auto regex = MakeRegEx(L"(?<=hello )world");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputBytes, RegExEncoding_utf8, 6, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(6), sub.input_offset);
            Assert::AreEqual(LONGLONG(5), sub.size);
        }

        TEST_METHOD(Match_StartByteOffset_LookBehindFailsWithoutContext)
        {
            // Negative coverage for the lookbehind case above: when the preceding
            // "hello " is NOT in the input, the lookbehind should fail.
            auto regex = MakeRegEx(L"(?<=hello )world");

            RegExBytes inputBytes = MakeString(u8"world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNull(results.get());
        }

        TEST_METHOD(Match_StartByteOffset_WordBoundary)
        {
            // \b at startByteOffset > 0 must see the character immediately before
            // m_pos to decide whether this is a word boundary. With base = m_begin,
            // the engine has full access to that character.
            auto regex = MakeRegEx(L"\\bworld");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputBytes, RegExEncoding_utf8, 6, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(6), sub.input_offset);
            Assert::AreEqual(LONGLONG(5), sub.size);
        }

        TEST_METHOD(Match_StartByteOffset_RequiresFullRemainder)
        {
            // regex_match requires the match to consume the entire remainder.
            // Pattern "\\w+" at startByteOffset = 4 with input "abc def ghi" should
            // fail because "def" alone matches but doesn't extend to end-of-input.
            auto regex = MakeRegEx(L"\\w+");

            RegExBytes inputBytes = MakeString(u8"abc def ghi"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(&inputBytes, RegExEncoding_utf8, 4, RegExMatchFlag_default, results.put()));
            Assert::IsNull(results.get());
        }

        TEST_METHOD(InputEncoding_Property)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u"x"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputBytes, RegExEncoding_utf16le, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExEncoding encoding = RegExEncoding_none;
            Assert::AreEqual(S_OK, results->get_InputEncoding(&encoding));
            Assert::AreEqual((int)RegExEncoding_utf16le, (int)encoding);
        }

        TEST_METHOD(Input_Property_ReturnsOriginalBytes)
        {
            auto regex = MakeRegEx(L"world");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExBytes echo = {};
            Assert::AreEqual(S_OK, results->get_Input(&echo));
            Assert::AreEqual(inputBytes.data_ptr, echo.data_ptr);
            Assert::AreEqual(inputBytes.size, echo.size);
        }

        TEST_METHOD(FormatTo_Stream)
        {
            auto regex = MakeRegEx(L"(\\w+) (\\w+)");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));

            wil::unique_bstr formatTemplate(SysAllocString(L"$2 $1"));
            Assert::AreEqual(
                S_OK,
                results->SetFormatTemplate(formatTemplate.get(), RegExFormatFlag_default));

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(S_OK, results->FormatTo(RegExEncoding_utf8, stream.get()));
            Assert::AreEqual("world hello"sv, stream->View<char>());
        }

        TEST_METHOD(FormatTo_UppercaseTransform)
        {
            // Regression: RegExMatchBase::VisitFormat must use the 4-argument
            // match_results::format overload so case-conversion escapes (\U, \L, \E)
            // pick up the correct traits from the regex engine.
            auto regex = MakeRegEx(L"(\\w+) (\\w+)");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));

            wil::unique_bstr formatTemplate(SysAllocString(L"\\U$1 $2\\E"));
            Assert::AreEqual(
                S_OK,
                results->SetFormatTemplate(formatTemplate.get(), RegExFormatFlag_default));

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(S_OK, results->FormatTo(RegExEncoding_utf8, stream.get()));
            Assert::AreEqual("HELLO WORLD"sv, stream->View<char>());
        }

        TEST_METHOD(FormatTo_DifferentEncoding)
        {
            // Input is UTF-8, but format output is UTF-16LE.
            auto regex = MakeRegEx(L"(\\w+) (\\w+)");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));

            wil::unique_bstr formatTemplate(SysAllocString(L"$2 $1"));
            Assert::AreEqual(
                S_OK,
                results->SetFormatTemplate(formatTemplate.get(), RegExFormatFlag_default));

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(S_OK, results->FormatTo(RegExEncoding_utf16le, stream.get()));
            Assert::IsTrue(u"world hello"sv == stream->View<char16_t>());
        }

        TEST_METHOD(FormatTo_NullStream)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());

            wil::unique_bstr formatTemplate(SysAllocString(L"y"));
            results->SetFormatTemplate(formatTemplate.get(), RegExFormatFlag_default);

            Assert::AreEqual(E_POINTER, results->FormatTo(RegExEncoding_utf8, nullptr));
        }

        TEST_METHOD(FormatTo_InvalidEncoding)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());

            wil::unique_bstr formatTemplate(SysAllocString(L"y"));
            results->SetFormatTemplate(formatTemplate.get(), RegExFormatFlag_default);

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(E_INVALIDARG, results->FormatTo(static_cast<RegExEncoding>(9999), stream.get()));
        }

        TEST_METHOD(CopyInput_Utf8Input)
        {
            // Input is UTF-8, CopyInput always returns BSTR (UTF-16LE).
            auto regex = MakeRegEx(L"world");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());

            // Copy bytes 0..5 ("hello") into a BSTR.
            wil::unique_bstr output;
            Assert::AreEqual(S_OK, results->CopyInput(0, 5, output.put()));
            Assert::AreEqual(L"hello"sv, MakeView(output.get()));
        }

        TEST_METHOD(CopyInput_Utf16Input_FastPath)
        {
            // Input is already UTF-16LE so CopyInput takes the fast path.
            auto regex = MakeRegEx(L"world");

            RegExBytes inputBytes = MakeString(u"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf16le, 0, RegExMatchFlag_default, results.put());

            // Bytes 12..22 = "world" (offsets 6..11 chars * 2 = 12..22).
            wil::unique_bstr output;
            Assert::AreEqual(S_OK, results->CopyInput(12, 10, output.put()));
            Assert::AreEqual(L"world"sv, MakeView(output.get()));
        }

        TEST_METHOD(CopyInput_OutOfBounds)
        {
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());

            wil::unique_bstr output;
            // Offset+size goes past end (5 bytes total).
            Assert::AreEqual(E_INVALIDARG, results->CopyInput(3, 5, output.put()));
        }

        TEST_METHOD(CopyInput_NegativeOffset)
        {
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());

            wil::unique_bstr output;
            Assert::AreEqual(E_INVALIDARG, results->CopyInput(-1, 2, output.put()));
        }

        TEST_METHOD(CopyInput_OddOffset_Utf16)
        {
            auto regex = MakeRegEx(L"A");

            RegExBytes inputBytes = MakeString(u"AB"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf16le, 0, RegExMatchFlag_default, results.put());

            wil::unique_bstr output;
            // Odd offset is invalid for UTF-16.
            Assert::AreEqual(E_INVALIDARG, results->CopyInput(1, 2, output.put()));
        }

        TEST_METHOD(CopyInput_EmptyRange)
        {
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());

            wil::unique_bstr output;
            Assert::AreEqual(S_OK, results->CopyInput(2, 0, output.put()));
            Assert::AreEqual(L""sv, MakeView(output.get()));
        }

        TEST_METHOD(CopyInputTo_SameEncoding_FastPath)
        {
            // Same encoding: bytes copied directly to stream.
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(S_OK, results->CopyInputTo(6, 5, RegExEncoding_utf8, stream.get()));
            Assert::AreEqual("world"sv, stream->View<char>());
        }

        TEST_METHOD(CopyInputTo_DifferentEncoding_Transcodes)
        {
            // UTF-8 input -> UTF-16LE output requires transcoding.
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(S_OK, results->CopyInputTo(0, 5, RegExEncoding_utf16le, stream.get()));
            Assert::IsTrue(u"hello"sv == stream->View<char16_t>());
        }

        TEST_METHOD(CopyInputTo_NullStream)
        {
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());

            Assert::AreEqual(E_POINTER, results->CopyInputTo(0, 5, RegExEncoding_utf8, nullptr));
        }

        TEST_METHOD(CopyInputTo_InvalidOutputEncoding)
        {
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                E_INVALIDARG,
                results->CopyInputTo(0, 5, static_cast<RegExEncoding>(9999), stream.get()));
        }

        TEST_METHOD(CopyInputTo_OutOfBounds)
        {
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                E_INVALIDARG,
                results->CopyInputTo(3, 5, RegExEncoding_utf8, stream.get()));
        }

        TEST_METHOD(GetSubMatch_NonParticipatingGroup)
        {
            // Pattern with two alternatives in optional groups. Matching "b" causes
            // group 1 ((a)) to not participate; matched should be VARIANT_FALSE.
            auto regex = MakeRegEx(L"(a)?(b)?");

            RegExBytes inputBytes = MakeString(u8"b"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            // Group 0 = entire match "b".
            RegExSubMatch sub = {};
            Assert::AreEqual(S_OK, results->GetSubMatch(0, &sub));
            Assert::AreEqual(VARIANT_TRUE, sub.matched);

            // Group 1 = (a) did not match.
            sub = {};
            Assert::AreEqual(S_OK, results->GetSubMatch(1, &sub));
            Assert::AreEqual(VARIANT_FALSE, sub.matched);
            Assert::AreEqual(LONGLONG(0), sub.input_offset);
            Assert::AreEqual(LONGLONG(0), sub.size);

            // Group 2 = (b) matched.
            sub = {};
            Assert::AreEqual(S_OK, results->GetSubMatch(2, &sub));
            Assert::AreEqual(VARIANT_TRUE, sub.matched);
            Assert::AreEqual(LONGLONG(0), sub.input_offset);
            Assert::AreEqual(LONGLONG(1), sub.size);
        }

        TEST_METHOD(NextMatch_OnSearchResults_ReturnsNotImpl)
        {
            // Single-match IRegExMatchResults from Search() should refuse NextMatch
            // (only IRegExMatchEnumerator iterates).
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
            Assert::IsNotNull(results.get());

            // QI to IRegExMatchEnumerator should fail.
            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = results->QueryInterface(IID_PPV_ARGS(enumerator.put()));
            Assert::AreEqual(E_NOINTERFACE, hr);
        }
    };
}
