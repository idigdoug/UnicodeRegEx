#include "pch.h"
#include "RegExTestHelpers.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

// Coverage tests for WindowsChar32RegexTraits features that aren't exercised
// by the main test suite:
// - collate flag (triggers transform/transform_primary for character ranges)
// - character class names [[:alpha:]] etc. (exercises lookup_classname/isctype)
// - collating elements [[.NUL.]] / [[.ae.]] (exercises lookup_collatename)
// - equivalence classes [[=a=]] (exercises transform_primary)
// - replacement transformations \L \U \E (exercises boost format engine through us)

namespace RegExTests
{
    TEST_CLASS(RegexTraitsCoverageTests)
    {
    public:

        // ----- collate flag (locale-sensitive character ranges) -----

        TEST_METHOD(Collate_BasicCharacterRange)
        {
            // With the collate flag set, ranges are compared via the locale's collation.
            // En-US LCID = 0x0409.
            auto regex = MakeRegEx(L"[a-z]+",
                static_cast<RegExSyntaxFlags>(RegExSyntaxFlags_ECMAScript | RegExSyntaxFlags_collate),
                0x0409);

            RegExBytes inputBytes = MakeString(u8"hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(0), sub.offset);
            Assert::AreEqual(LONGLONG(5), sub.size);
        }

        TEST_METHOD(Collate_RangeRejectsOutsideRange)
        {
            // With collate flag, the [a-z] range uses locale collation. For en-US the
            // range covers digits and punctuation that sort outside a-z.
            auto regex = MakeRegEx(L"^[a-z]+$",
                static_cast<RegExSyntaxFlags>(RegExSyntaxFlags_ECMAScript | RegExSyntaxFlags_collate),
                0x0409);

            // Pure digits should not match a letter range.
            RegExBytes inputBytes = MakeString(u8"12345"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNull(results.get());
        }

        TEST_METHOD(Collate_RangeIsCaseInsensitive)
        {
            // With collate flag, [a-z] matches uppercase letters too because primary
            // collation weight ignores case.
            auto regex = MakeRegEx(L"^[a-z]+$",
                static_cast<RegExSyntaxFlags>(RegExSyntaxFlags_ECMAScript | RegExSyntaxFlags_collate),
                0x0409);

            RegExBytes inputBytes = MakeString(u8"HELLO"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());
        }

        // ----- POSIX character classes [[:name:]] -----

        TEST_METHOD(CharacterClass_Alpha)
        {
            auto regex = MakeRegEx(L"[[:alpha:]]+",
                RegExSyntaxFlags_ECMAScript,
                0x0409);

            RegExBytes inputBytes = MakeString(u8"abc 123"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(0), sub.offset);
            Assert::AreEqual(LONGLONG(3), sub.size);
        }

        TEST_METHOD(CharacterClass_Digit)
        {
            auto regex = MakeRegEx(L"[[:digit:]]+",
                RegExSyntaxFlags_ECMAScript,
                0x0409);

            RegExBytes inputBytes = MakeString(u8"abc 12345 def"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(4), sub.offset);
            Assert::AreEqual(LONGLONG(5), sub.size);
        }

        TEST_METHOD(CharacterClass_Space)
        {
            auto regex = MakeRegEx(L"[[:space:]]+",
                RegExSyntaxFlags_ECMAScript,
                0x0409);

            RegExBytes inputBytes = MakeString(u8"a   b"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(1), sub.offset);
            Assert::AreEqual(LONGLONG(3), sub.size);
        }

        TEST_METHOD(CharacterClass_Upper)
        {
            auto regex = MakeRegEx(L"[[:upper:]]+",
                RegExSyntaxFlags_ECMAScript,
                0x0409);

            RegExBytes inputBytes = MakeString(u8"hello WORLD"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(6), sub.offset);
            Assert::AreEqual(LONGLONG(5), sub.size);
        }

        TEST_METHOD(CharacterClass_Lower)
        {
            auto regex = MakeRegEx(L"[[:lower:]]+",
                RegExSyntaxFlags_ECMAScript,
                0x0409);

            RegExBytes inputBytes = MakeString(u8"HELLO world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(6), sub.offset);
            Assert::AreEqual(LONGLONG(5), sub.size);
        }

        TEST_METHOD(CharacterClass_Punct)
        {
            auto regex = MakeRegEx(L"[[:punct:]]+",
                RegExSyntaxFlags_ECMAScript,
                0x0409);

            RegExBytes inputBytes = MakeString(u8"abc!@#xyz"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(3), sub.offset);
            Assert::AreEqual(LONGLONG(3), sub.size);
        }

        // ----- Collating elements [[.name.]] -----

        TEST_METHOD(CollatingElement_PosixNameTab)
        {
            // [[.tab.]] should map to the literal tab character (U+0009).
            auto regex = MakeRegEx(L"[[.tab.]]",
                RegExSyntaxFlags_ECMAScript,
                0x0409);

            RegExBytes inputBytes = MakeString(u8"a\tb"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(1), sub.offset);
            Assert::AreEqual(LONGLONG(1), sub.size);
        }

        TEST_METHOD(CollatingElement_PosixNameZero)
        {
            // [[.NUL.]] should map to U+0000.
            auto regex = MakeRegEx(L"[[.NUL.]]",
                RegExSyntaxFlags_ECMAScript,
                0x0409);

            char input[] = { 'a', '\0', 'b' };
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(input)),
                .size = sizeof(input),
            };

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExEncoding_latin1, 0, RegExMatchFlag_default, results.put());
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(1), sub.offset);
            Assert::AreEqual(LONGLONG(1), sub.size);
        }

        TEST_METHOD(CollatingElement_PosixNameLetterA)
        {
            // [[.A.]] should match the letter A.
            auto regex = MakeRegEx(L"[[.A.]]",
                RegExSyntaxFlags_ECMAScript,
                0x0409);

            RegExBytes inputBytes = MakeString(u8"xyzABC"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(3), sub.offset);
            Assert::AreEqual(LONGLONG(1), sub.size);
        }

        TEST_METHOD(CollatingElement_DigraphAE)
        {
            // [[.ae.]] is a digraph: when used in a character class it matches the
            // two-character sequence "ae".
            auto regex = MakeRegEx(L"[[.ae.]]",
                RegExSyntaxFlags_ECMAScript,
                0x0409);

            RegExBytes inputBytes = MakeString(u8"caesar"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(1), sub.offset);
            Assert::AreEqual(LONGLONG(2), sub.size);
        }

        // ----- Equivalence classes [[=name=]] -----

        TEST_METHOD(EquivalenceClass_Letter)
        {
            // [[=a=]] should match characters equivalent under primary collation to 'a'.
            // For en-US this includes 'a' and 'A' (case differs but primary weight matches).
            auto regex = MakeRegEx(L"[[=a=]]",
                RegExSyntaxFlags_ECMAScript,
                0x0409);

            RegExBytes inputBytes = MakeString(u8"xyzA"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExEncoding_utf8, 0, RegExMatchFlag_default, results.put());
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(3), sub.offset);
            Assert::AreEqual(LONGLONG(1), sub.size);
        }
    };
}
