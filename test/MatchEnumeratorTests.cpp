#include "pch.h"
#include "RegExTestHelpers.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

namespace RegExTests
{
    TEST_CLASS(MatchEnumeratorTests)
    {
    public:

        TEST_METHOD(BasicMatch_Utf8)
        {
            auto regex = MakeRegEx(L"world", RegExSyntaxFlags_ECMAScript, 0x0409);

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, enumerator.put());
            Assert::AreEqual(S_OK, hr);
            Assert::IsNotNull(enumerator.get());

            // First NextMatch should find "world" at offset 6.
            VARIANT_BOOL found = VARIANT_FALSE;
            hr = enumerator->NextMatch(&found);
            Assert::AreEqual(S_OK, hr);
            Assert::IsTrue(found != 0);

            UINT32 count = 0;
            enumerator->get_SubMatchCount(&count);
            Assert::IsTrue(count >= 1); // at least group 0

            RegExSubMatch submatch = {};
            hr = enumerator->GetSubMatch(0, &submatch);
            Assert::AreEqual(S_OK, hr);
            Assert::AreEqual(VARIANT_TRUE, submatch.matched);
            Assert::AreEqual(LONGLONG(6), submatch.input_offset);
            Assert::AreEqual(LONGLONG(5), submatch.size);

            // Second NextMatch should indicate no more matches.
            hr = enumerator->NextMatch(&found);
            Assert::AreEqual(S_OK, hr);
            Assert::IsFalse(found != 0);
        }

        TEST_METHOD(MultipleMatches)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"abc 123 def 456"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, enumerator.put());

            // First match: "123" at offset 4
            VARIANT_BOOL found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsTrue(found != 0);
            RegExSubMatch sub = {};
            enumerator->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(4), sub.input_offset);
            Assert::AreEqual(LONGLONG(3), sub.size);

            // Second match: "456" at offset 12
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsTrue(found != 0);
            enumerator->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(12), sub.input_offset);
            Assert::AreEqual(LONGLONG(3), sub.size);

            // No more
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsFalse(found != 0);
        }

        TEST_METHOD(NoMatch)
        {
            auto regex = MakeRegEx(L"xyz");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, enumerator.put());

            VARIANT_BOOL found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsFalse(found != 0);
        }

        TEST_METHOD(CaptureGroups)
        {
            auto regex = MakeRegEx(L"(\\w+)@(\\w+)");

            RegExBytes inputBytes = MakeString(u8"$user@host!"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            Assert::AreEqual(
                S_OK,
                regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, enumerator.put()));

            VARIANT_BOOL found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsTrue(found != 0);

            UINT32 count = 0;
            enumerator->get_SubMatchCount(&count);
            Assert::AreEqual(UINT32(3), count); // group 0, 1, 2

            RegExSubMatch sub = {};
            RegExBytes str;

            // Group 0: "user@host" at offset 1, length 9

            Assert::AreEqual(
                S_OK,
                enumerator->GetSubMatch(0, &sub));
            Assert::AreEqual(VARIANT_TRUE, sub.matched);
            Assert::AreEqual(LONGLONG(1), sub.input_offset);
            Assert::AreEqual(LONGLONG(9), sub.size);

            // Group 1: "user" at offset 1, length 4

            Assert::AreEqual(
                S_OK,
                enumerator->GetSubMatch(1, &sub));
            Assert::AreEqual(VARIANT_TRUE, sub.matched);
            Assert::AreEqual(LONGLONG(1), sub.input_offset);
            Assert::AreEqual(LONGLONG(4), sub.size);

            // Group 2: "host" at offset 6, length 4

            Assert::AreEqual(
                S_OK,
                enumerator->GetSubMatch(2, &sub));
            Assert::AreEqual(VARIANT_TRUE, sub.matched);
            Assert::AreEqual(LONGLONG(6), sub.input_offset);
            Assert::AreEqual(LONGLONG(4), sub.size);
        }

        TEST_METHOD(Utf16_Turkish)
        {
            // Turkish has special casing rules for 'i' and 'I'. This test ensures that i <==> İ and I <==> ı work correctly.
            // Turkish locale: LCID 0x041F
            // In Turkish: lowercase 'i' (U+0069) has uppercase 'İ' (U+0130)
            //             uppercase 'I' (U+0049) has lowercase 'ı' (U+0131)

            // Pattern "i" with icase in Turkish locale should match İ but NOT I
            auto regex_i = MakeRegEx(L"i",
                static_cast<RegExSyntaxFlags>(RegExSyntaxFlags_ECMAScript | RegExSyntaxFlags_icase),
                0x041F);

            // "İ" (U+0130) should match "i" case-insensitively in Turkish
            RegExBytes inputBytes_idot = MakeString(u"\u0130"sv);

            wil::com_ptr<IRegExMatchEnumerator> enum_idot;
            Assert::AreEqual(S_OK, regex_i->EnumerateMatches(&inputBytes_idot, RegExEncoding_utf16le, 0, RegExMatchFlag_default, enum_idot.put()));

            VARIANT_BOOL found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enum_idot->NextMatch(&found));
            Assert::IsTrue(found != 0, L"Turkish 'i' should match '\u0130' (İ) case-insensitively");

            // "I" (U+0049) should NOT match "i" case-insensitively in Turkish
            RegExBytes inputBytes_I = MakeString(u"I"sv);

            wil::com_ptr<IRegExMatchEnumerator> enum_I;
            Assert::AreEqual(S_OK, regex_i->EnumerateMatches(&inputBytes_I, RegExEncoding_utf16le, 0, RegExMatchFlag_default, enum_I.put()));

            found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enum_I->NextMatch(&found));
            Assert::IsFalse(found != 0, L"Turkish 'i' should NOT match 'I' case-insensitively");

            // Pattern "I" with icase in Turkish locale should match ı (U+0131) but NOT i
            auto regex_I = MakeRegEx(L"I",
                static_cast<RegExSyntaxFlags>(RegExSyntaxFlags_ECMAScript | RegExSyntaxFlags_icase),
                0x041F);

            // "ı" (U+0131) should match "I" case-insensitively in Turkish
            RegExBytes inputBytes_dotless = MakeString(u"\u0131"sv);

            wil::com_ptr<IRegExMatchEnumerator> enum_dotless;
            Assert::AreEqual(S_OK, regex_I->EnumerateMatches(&inputBytes_dotless, RegExEncoding_utf16le, 0, RegExMatchFlag_default, enum_dotless.put()));

            found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enum_dotless->NextMatch(&found));
            Assert::IsTrue(found != 0, L"Turkish 'I' should match '\u0131' (ı) case-insensitively");

            // "i" (U+0069) should NOT match "I" case-insensitively in Turkish
            RegExBytes inputBytes_latin_i = MakeString(u"i"sv);

            wil::com_ptr<IRegExMatchEnumerator> enum_latin_i;
            Assert::AreEqual(S_OK, regex_I->EnumerateMatches(&inputBytes_latin_i, RegExEncoding_utf16le, 0, RegExMatchFlag_default, enum_latin_i.put()));

            found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enum_latin_i->NextMatch(&found));
            Assert::IsFalse(found != 0, L"Turkish 'I' should NOT match 'i' case-insensitively");
        }

        TEST_METHOD(Utf16LE_Match)
        {
            auto regex = MakeRegEx(L"world");

            RegExBytes inputBytes = MakeString(u"hello world"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(&inputBytes, RegExEncoding_utf16le, 0, RegExMatchFlag_default, enumerator.put());

            VARIANT_BOOL found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsTrue(found != 0);
            RegExSubMatch sub = {};
            enumerator->GetSubMatch(0, &sub);
            Assert::AreEqual(VARIANT_TRUE, sub.matched);
            Assert::AreEqual(LONGLONG(6 * 2), sub.input_offset); // 6 chars * 2 bytes
            Assert::AreEqual(LONGLONG(5 * 2), sub.size);
        }

        TEST_METHOD(GetState_Transitions)
        {
            auto regex = MakeRegEx(L"a");

            RegExBytes inputBytes = MakeString(u8"a"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, enumerator.put());

            RegExEnumerationState state = {};
            enumerator->get_State(&state);
            Assert::AreEqual((int)RegExEnumerationState_not_started, (int)state);

            VARIANT_BOOL found = VARIANT_FALSE;
            enumerator->NextMatch(&found);
            enumerator->get_State(&state);
            Assert::AreEqual((int)RegExEnumerationState_enumerating, (int)state);

            enumerator->NextMatch(&found); // no more matches
            enumerator->get_State(&state);
            Assert::AreEqual((int)RegExEnumerationState_finished, (int)state);
        }

        TEST_METHOD(FormatReplacement_Utf8)
        {
            auto regex = MakeRegEx(L"(\\w+)@(\\w+)");

            RegExBytes inputBytes = MakeString(u8"user@host"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, enumerator.put());

            VARIANT_BOOL found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsTrue(found != 0);

            wil::unique_bstr replacement(SysAllocString(L"$2@$1"));
            HRESULT hr = enumerator->SetFormatTemplate(replacement.get(), RegExFormatFlag_default);
            Assert::AreEqual(S_OK, hr);

            RegExBytes output = {};
            hr = enumerator->Format(RegExEncoding_utf8, &output);
            Assert::AreEqual(S_OK, hr);
            Assert::AreEqual(LONGLONG(9), output.size); // "host@user" = 9 bytes

            Assert::AreEqual("host@user"sv, MakeView<char>(output));
        }

        TEST_METHOD(EmptyMatches_NoInfiniteLoop)
        {
            // Test that empty matches don't cause infinite loops
            // Pattern a* can match zero or more 'a's, including empty matches
            auto regex = MakeRegEx(L"a*");

            RegExBytes inputBytes = MakeString(u8"b"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, enumerator.put());

            // Should find matches but not loop infinitely
            VARIANT_BOOL found = VARIANT_FALSE;
            int matchCount = 0;

            while (matchCount < 100) // Safety limit
            {
                Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
                if (!found) break;
                matchCount++;
            }

            // Should have terminated naturally (found == FALSE), not hit the safety limit
            Assert::IsFalse(found != 0);
            Assert::IsTrue(matchCount < 100);
            Assert::IsTrue(matchCount > 0); // Should find at least one match
        }

        TEST_METHOD(EmptyMatches_WordBoundaries)
        {
            // Test that word boundaries (\b) are found correctly
            auto regex = MakeRegEx(L"\\b");

            RegExBytes inputBytes = MakeString(u8"a b"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, enumerator.put());

            VARIANT_BOOL found = VARIANT_FALSE;
            RegExSubMatch sub = {};
            int matchCount = 0;

            while (matchCount < 100) // Safety limit
            {
                Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
                if (!found) break;

                enumerator->GetSubMatch(0, &sub);
                Assert::AreEqual(LONGLONG(0), sub.size); // Word boundaries are always empty
                matchCount++;
            }

            // Should find word boundaries and terminate naturally
            Assert::IsFalse(found != 0);
            Assert::IsTrue(matchCount >= 2); // At least 2 word boundaries in "a b"
            Assert::IsTrue(matchCount < 100); // Should not hit safety limit
        }

        TEST_METHOD(EmptyMatch_AtStartOfInput)
        {
            // Regression: the first call to VisitNextMatch must NOT set match_prev_avail.
            // Pattern "a*" on "bbb" must match empty at offset 0 (the regex_iterator
            // constructor's initial regex_search with original flags only).
            // If match_prev_avail were applied on the first search, the engine would be
            // told that *(begin - 1) is dereferenceable, which is false at start of input.
            auto regex = MakeRegEx(L"a*");

            RegExBytes inputBytes = MakeString(u8"bbb"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            Assert::AreEqual(
                S_OK,
                regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, enumerator.put()));

            // First match: empty at offset 0.
            VARIANT_BOOL found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsTrue(found != 0);

            RegExSubMatch sub = {};
            enumerator->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(0), sub.input_offset);
            Assert::AreEqual(LONGLONG(0), sub.size);

            // Continue iterating; per the standard's operator++ semantics, "a*" against
            // "bbb" yields an empty match at every position 0..3 (one past end), for 4 total.
            int matchCount = 1;
            LONGLONG lastOffset = 0;
            while (matchCount < 100)
            {
                Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
                if (!found) break;
                enumerator->GetSubMatch(0, &sub);
                Assert::AreEqual(LONGLONG(0), sub.size);
                Assert::IsTrue(sub.input_offset > lastOffset); // Must make forward progress.
                lastOffset = sub.input_offset;
                matchCount++;
            }
            Assert::IsFalse(found != 0);
            Assert::AreEqual(4, matchCount);
            Assert::AreEqual(LONGLONG(3), lastOffset); // Last empty match is at end-of-input.
        }

        TEST_METHOD(ZeroLengthRetry_AfterNonEmptyMatch)
        {
            // Exercises the persisted match_prev_avail across zero-length retries.
            // Pattern "a+|\\b" on "aa " matches (ECMAScript leftmost-first alternation):
            //   1. "aa" at 0..2  (non-zero-length; a+ wins at position 0)
            //                     This sets match_prev_avail in m_matchFlags.
            //   2. \b at 2       (zero-length; 'a' is word, ' ' is non-word, so there is
            //                     a word boundary here. The engine must be able to see
            //                     *(start-1) == 'a' via match_prev_avail to detect it.)
            auto regex = MakeRegEx(L"a+|\\b");

            RegExBytes inputBytes = MakeString(u8"aa "sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            Assert::AreEqual(
                S_OK,
                regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, enumerator.put()));

            VARIANT_BOOL found = VARIANT_FALSE;
            RegExSubMatch sub = {};

            // Match 1: "aa" at 0 (non-empty). Sets match_prev_avail going forward.
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsTrue(found != 0);
            enumerator->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(0), sub.input_offset);
            Assert::AreEqual(LONGLONG(2), sub.size);

            // Match 2: \b at 2 (between 'a' and ' '). Requires match_prev_avail to detect.
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsTrue(found != 0);
            enumerator->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(2), sub.input_offset);
            Assert::AreEqual(LONGLONG(0), sub.size);

            // No more matches in " ".
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsFalse(found != 0);
        }

        TEST_METHOD(RejectsMatchPrevAvail)
        {
            // EnumerateMatches must reject match_prev_avail from the caller; the
            // enumerator manages that flag itself per the C++ standard's regex_iterator
            // semantics.
            auto regex = MakeRegEx(L"a");

            RegExBytes inputBytes = MakeString(u8"a"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->EnumerateMatches(
                &inputBytes, RegExEncoding_utf8, 0, static_cast<RegExMatchFlags>(1 << 8), enumerator.put());
            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(enumerator.get());
        }

        TEST_METHOD(RejectsNoExcept)
        {
            // CreateRegEx must reject boost::regex_constants::no_except (bit 1<<18),
            // because that flag would suppress pattern errors and leave a broken regex
            // that we couldn't report. The rejection happens up front regardless of
            // whether the pattern itself is valid.
            RegExErrorCode errorCode = RegExErrorCode_ok;
            wil::com_ptr<IRegEx> regex;
            HRESULT hr = TryMakeRegEx(
                L"hello",
                static_cast<RegExSyntaxFlags>(RegExSyntaxFlags_ECMAScript | (1 << 18)),
                LOCALE_NEUTRAL,
                &errorCode, regex);

            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(regex.get());
            Assert::IsTrue(errorCode != RegExErrorCode_ok);
        }

        TEST_METHOD(StartByteOffset_SkipsEarlierContent)
        {
            // Pattern "hello" exists at offset 0, but starting at offset 5 should skip it.
            auto regex = MakeRegEx(L"hello");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 5, RegExMatchFlag_default, enumerator.put());
            Assert::AreEqual(S_OK, hr);

            VARIANT_BOOL found = VARIANT_FALSE;
            enumerator->NextMatch(&found);
            Assert::IsFalse(found != 0);
        }

        TEST_METHOD(StartByteOffset_MatchesAtOffset)
        {
            // Pattern "world" is at offset 6. Starting at offset 6 should find it.
            auto regex = MakeRegEx(L"world");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 6, RegExMatchFlag_default, enumerator.put());
            Assert::AreEqual(S_OK, hr);

            VARIANT_BOOL found = VARIANT_FALSE;
            enumerator->NextMatch(&found);
            Assert::IsTrue(found != 0);

            RegExSubMatch sub = {};
            enumerator->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(6), sub.input_offset);
            Assert::AreEqual(LONGLONG(5), sub.size);
        }

        TEST_METHOD(StartByteOffset_LookBehind)
        {
            // Pattern "(?<=hello )world" uses lookbehind. Starting at offset 6
            // (the 'w') should still match because begin..pos provides lookbehind context.
            auto regex = MakeRegEx(L"(?<=hello )world");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 6, RegExMatchFlag_default, enumerator.put());
            Assert::AreEqual(S_OK, hr);

            VARIANT_BOOL found = VARIANT_FALSE;
            enumerator->NextMatch(&found);
            Assert::IsTrue(found != 0);

            RegExSubMatch sub = {};
            enumerator->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(6), sub.input_offset);
            Assert::AreEqual(LONGLONG(5), sub.size);
        }

        TEST_METHOD(StartByteOffset_LookBehindFailsWithoutContext)
        {
            // Same lookbehind pattern, but the input only contains "world" with no
            // preceding "hello " — lookbehind should fail.
            auto regex = MakeRegEx(L"(?<=hello )world");

            RegExBytes inputBytes = MakeString(u8"world"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, enumerator.put());
            Assert::AreEqual(S_OK, hr);

            VARIANT_BOOL found = VARIANT_FALSE;
            enumerator->NextMatch(&found);
            Assert::IsFalse(found != 0);
        }

        TEST_METHOD(StartByteOffset_InvalidOffsetTooLarge)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 6, RegExMatchFlag_default, enumerator.put());
            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(enumerator.get());
        }

        TEST_METHOD(StartByteOffset_InvalidMidSequence_Utf8)
        {
            // "é" is 2 bytes in UTF-8 (0xC3 0xA9). Offset 1 points into the middle.
            auto regex = MakeRegEx(L"x");

            auto input = u8"é"sv;
            RegExBytes inputBytes = MakeString(input);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 1, RegExMatchFlag_default, enumerator.put());
            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(enumerator.get());
        }

        TEST_METHOD(StartByteOffset_InvalidMidSequence_Utf16)
        {
            // "😀" is a surrogate pair (4 bytes). Offset 2 points at the low surrogate.
            auto regex = MakeRegEx(L"x");

            auto input = u"😀"sv;
            RegExBytes inputBytes = MakeString(input);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->EnumerateMatches(&inputBytes, RegExEncoding_utf16le, 2, RegExMatchFlag_default, enumerator.put());
            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(enumerator.get());
        }

        TEST_METHOD(StartByteOffset_OddByteOffset_Utf16)
        {
            // Odd byte offset is not valid for UTF-16.
            auto regex = MakeRegEx(L"x");

            auto input = u"AB"sv;
            RegExBytes inputBytes = MakeString(input);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->EnumerateMatches(&inputBytes, RegExEncoding_utf16le, 1, RegExMatchFlag_default, enumerator.put());
            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(enumerator.get());
        }

        TEST_METHOD(StartByteOffset_AtEnd)
        {
            // Starting at the end of the input should be valid but find no matches.
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->EnumerateMatches(&inputBytes, RegExEncoding_utf8, 5, RegExMatchFlag_default, enumerator.put());
            Assert::AreEqual(S_OK, hr);

            VARIANT_BOOL found = VARIANT_FALSE;
            enumerator->NextMatch(&found);
            Assert::IsFalse(found != 0);
        }
    };
}
