#include "pch.h"
#include <RepStrRegEx.h>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

template<class CharT>
static constexpr RegExString
MakeString(std::basic_string_view<CharT> sv, RegExEncoding encoding)
{
    return {
        .data_ptr = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(sv.data())),
        .size = static_cast<LONGLONG>(sv.size() * sizeof(sv[0])),
        .encoding = encoding
    };
}

template<class CharT>
static constexpr std::basic_string_view<CharT>
MakeView(RegExString const& str) noexcept
{
    return std::basic_string_view<CharT>(
        reinterpret_cast<CharT const*>(str.data_ptr),
        static_cast<UINT_PTR>(str.size / sizeof(CharT)));
}

namespace RegExTests
{
    TEST_CLASS(RegExCreateTests)
    {
    public:

        TEST_METHOD(CreateSimpleRegex)
        {
            wil::unique_bstr pattern(SysAllocString(L"hello"));
            RegExErrorCode errorCode = RegExErrorCode_unknown;
            wil::com_ptr<IRegEx> regex;

            HRESULT hr = RepStrRegExCreate(
                pattern.get(), RegExSyntaxFlags_ECMAScript, LOCALE_NEUTRAL,
                &errorCode, regex.put());

            Assert::AreEqual(S_OK, hr);
            Assert::IsNotNull(regex.get());
            Assert::AreEqual((int)RegExErrorCode_ok, (int)errorCode);
        }

        TEST_METHOD(CreateInvalidRegex)
        {
            wil::unique_bstr pattern(SysAllocString(L"[invalid"));
            RegExErrorCode errorCode = RegExErrorCode_ok;
            wil::com_ptr<IRegEx> regex;

            HRESULT hr = RepStrRegExCreate(
                pattern.get(), RegExSyntaxFlags_ECMAScript, LOCALE_NEUTRAL,
                &errorCode, regex.put());

            Assert::AreEqual(E_INVALIDARG, hr);
            Assert::IsNull(regex.get());
            Assert::IsTrue(errorCode != RegExErrorCode_ok);
        }

        TEST_METHOD(CreateWithIcase)
        {
            wil::unique_bstr pattern(SysAllocString(L"hello"));
            wil::com_ptr<IRegEx> regex;

            HRESULT hr = RepStrRegExCreate(
                pattern.get(),
                static_cast<RegExSyntaxFlags>(RegExSyntaxFlags_ECMAScript | RegExSyntaxFlags_icase),
                LOCALE_INVARIANT,
                nullptr, regex.put());

            Assert::AreEqual(S_OK, hr);
            Assert::IsNotNull(regex.get());
        }

        TEST_METHOD(QueryInterface_IUnknown)
        {
            wil::unique_bstr pattern(SysAllocString(L"test"));
            wil::com_ptr<IRegEx> regex;
            RepStrRegExCreate(pattern.get(), RegExSyntaxFlags_ECMAScript, LOCALE_INVARIANT, nullptr, regex.put());

            wil::com_ptr<IUnknown> unk;
            HRESULT hr = regex->QueryInterface(IID_PPV_ARGS(unk.put()));
            Assert::AreEqual(S_OK, hr);
            Assert::IsNotNull(unk.get());
        }

        TEST_METHOD(RefCounting)
        {
            wil::unique_bstr pattern(SysAllocString(L"test"));
            IRegEx* raw = nullptr;
            RepStrRegExCreate(pattern.get(), RegExSyntaxFlags_ECMAScript, LOCALE_INVARIANT, nullptr, &raw);
            Assert::IsNotNull(raw);

            ULONG ref = raw->AddRef();
            Assert::IsTrue(ref >= 2);
            raw->Release();
            raw->Release(); // final release
        }
    };

    TEST_CLASS(MatchEnumeratorTests)
    {
    public:

        TEST_METHOD(BasicMatch_Utf8)
        {
            wil::unique_bstr pattern(SysAllocString(L"world"));
            wil::com_ptr<IRegEx> regex;
            RepStrRegExCreate(pattern.get(), RegExSyntaxFlags_ECMAScript, 0x0409, nullptr, regex.put());

            RegExString inputStr = MakeString("hello world"sv, RegExEncoding_utf8);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            HRESULT hr = regex->CreateMatchEnumerator(&inputStr, RegExMatchFlag_default, enumerator.put());
            Assert::AreEqual(S_OK, hr);
            Assert::IsNotNull(enumerator.get());

            // First NextMatch should find "world" at offset 6.
            VARIANT_BOOL found = VARIANT_FALSE;
            hr = enumerator->NextMatch(&found);
            Assert::AreEqual(S_OK, hr);
            Assert::IsTrue(found != 0);

            UINT32 count = 0;
            enumerator->GetSubMatchCount(&count);
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
            wil::unique_bstr pattern(SysAllocString(L"\\d+"));
            wil::com_ptr<IRegEx> regex;
            RepStrRegExCreate(pattern.get(), RegExSyntaxFlags_ECMAScript, 0, nullptr, regex.put());

            RegExString inputStr = MakeString("abc 123 def 456"sv, RegExEncoding_utf8);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->CreateMatchEnumerator(&inputStr, RegExMatchFlag_default, enumerator.put());

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
            wil::unique_bstr pattern(SysAllocString(L"xyz"));
            wil::com_ptr<IRegEx> regex;
            RepStrRegExCreate(pattern.get(), RegExSyntaxFlags_ECMAScript, 0, nullptr, regex.put());

            RegExString inputStr = MakeString("hello world"sv, RegExEncoding_utf8);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->CreateMatchEnumerator(&inputStr, RegExMatchFlag_default, enumerator.put());

            VARIANT_BOOL found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsFalse(found != 0);
        }

        TEST_METHOD(CaptureGroups)
        {
            wil::unique_bstr pattern(SysAllocString(L"(\\w+)@(\\w+)"));
            wil::com_ptr<IRegEx> regex;
            Assert::AreEqual(
                S_OK,
                RepStrRegExCreate(pattern.get(), RegExSyntaxFlags_ECMAScript, 0, nullptr, regex.put()));

            RegExString inputStr = MakeString("$user@host!"sv, RegExEncoding_utf8);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            Assert::AreEqual(
                S_OK,
                regex->CreateMatchEnumerator(&inputStr, RegExMatchFlag_default, enumerator.put()));

            VARIANT_BOOL found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsTrue(found != 0);

            UINT32 count = 0;
            enumerator->GetSubMatchCount(&count);
            Assert::AreEqual(UINT32(3), count); // group 0, 1, 2

            RegExSubMatch sub = {};
            RegExString str;

            // Group 0: "user@host" at offset 1, length 9

            Assert::AreEqual(
                S_OK,
                enumerator->GetSubMatch(0, &sub));
            Assert::AreEqual(VARIANT_TRUE, sub.matched);
            Assert::AreEqual(LONGLONG(1), sub.input_offset);
            Assert::AreEqual(LONGLONG(9), sub.size);

            Assert::AreEqual(
                S_OK,
                enumerator->GetSubMatchString(0, RegExEncoding_latin1, &str));
            Assert::IsTrue(RegExEncoding_latin1 == str.encoding);
            Assert::IsTrue("user@host"sv == MakeView<char>(str));

            // Group 1: "user" at offset 1, length 4

            Assert::AreEqual(
                S_OK,
                enumerator->GetSubMatch(1, &sub));
            Assert::AreEqual(VARIANT_TRUE, sub.matched);
            Assert::AreEqual(LONGLONG(1), sub.input_offset);
            Assert::AreEqual(LONGLONG(4), sub.size);

            Assert::AreEqual(
                S_OK,
                enumerator->GetSubMatchString(1, RegExEncoding_utf16le, &str));
            Assert::IsTrue(RegExEncoding_utf16le == str.encoding);
            Assert::IsTrue(u"user"sv == MakeView<char16_t>(str));

            // Group 2: "host" at offset 6, length 4

            Assert::AreEqual(
                S_OK,
                enumerator->GetSubMatch(2, &sub));
            Assert::AreEqual(VARIANT_TRUE, sub.matched);
            Assert::AreEqual(LONGLONG(6), sub.input_offset);
            Assert::AreEqual(LONGLONG(4), sub.size);

            Assert::AreEqual(
                S_OK,
                enumerator->GetSubMatchString(2, RegExEncoding_utf8, &str));
            Assert::IsTrue(RegExEncoding_utf8 == str.encoding);
            Assert::IsTrue("host"sv == MakeView<char>(str));
        }

        TEST_METHOD(Utf16_Turkish)
        {
            // Turkish has special casing rules for 'i' and 'I'. This test ensures that i <==> İ and I <==> ı work correctly.
            // Turkish locale: LCID 0x041F
            // In Turkish: lowercase 'i' (U+0069) has uppercase 'İ' (U+0130)
            //             uppercase 'I' (U+0049) has lowercase 'ı' (U+0131)

            // Pattern "i" with icase in Turkish locale should match İ but NOT I
            wil::unique_bstr pattern_i(SysAllocString(L"i"));
            wil::com_ptr<IRegEx> regex_i;
            Assert::AreEqual(
                S_OK,
                RepStrRegExCreate(
                    pattern_i.get(),
                    static_cast<RegExSyntaxFlags>(RegExSyntaxFlags_ECMAScript | RegExSyntaxFlags_icase),
                    0x041F,
                    nullptr, regex_i.put()));

            // "İ" (U+0130) should match "i" case-insensitively in Turkish
            RegExString inputStr_idot = MakeString(u"\u0130"sv, RegExEncoding_utf16le);

            wil::com_ptr<IRegExMatchEnumerator> enum_idot;
            Assert::AreEqual(S_OK, regex_i->CreateMatchEnumerator(&inputStr_idot, RegExMatchFlag_default, enum_idot.put()));

            VARIANT_BOOL found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enum_idot->NextMatch(&found));
            Assert::IsTrue(found != 0, L"Turkish 'i' should match '\u0130' (İ) case-insensitively");

            // "I" (U+0049) should NOT match "i" case-insensitively in Turkish
            RegExString inputStr_I = MakeString(u"I"sv, RegExEncoding_utf16le);

            wil::com_ptr<IRegExMatchEnumerator> enum_I;
            Assert::AreEqual(S_OK, regex_i->CreateMatchEnumerator(&inputStr_I, RegExMatchFlag_default, enum_I.put()));

            found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enum_I->NextMatch(&found));
            Assert::IsFalse(found != 0, L"Turkish 'i' should NOT match 'I' case-insensitively");

            // Pattern "I" with icase in Turkish locale should match ı (U+0131) but NOT i
            wil::unique_bstr pattern_I(SysAllocString(L"I"));
            wil::com_ptr<IRegEx> regex_I;
            Assert::AreEqual(
                S_OK,
                RepStrRegExCreate(
                    pattern_I.get(),
                    static_cast<RegExSyntaxFlags>(RegExSyntaxFlags_ECMAScript | RegExSyntaxFlags_icase),
                    0x041F,
                    nullptr, regex_I.put()));

            // "ı" (U+0131) should match "I" case-insensitively in Turkish
            RegExString inputStr_dotless = MakeString(u"\u0131"sv, RegExEncoding_utf16le);

            wil::com_ptr<IRegExMatchEnumerator> enum_dotless;
            Assert::AreEqual(S_OK, regex_I->CreateMatchEnumerator(&inputStr_dotless, RegExMatchFlag_default, enum_dotless.put()));

            found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enum_dotless->NextMatch(&found));
            Assert::IsTrue(found != 0, L"Turkish 'I' should match '\u0131' (ı) case-insensitively");

            // "i" (U+0069) should NOT match "I" case-insensitively in Turkish
            RegExString inputStr_latin_i = MakeString(u"i"sv, RegExEncoding_utf16le);

            wil::com_ptr<IRegExMatchEnumerator> enum_latin_i;
            Assert::AreEqual(S_OK, regex_I->CreateMatchEnumerator(&inputStr_latin_i, RegExMatchFlag_default, enum_latin_i.put()));

            found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enum_latin_i->NextMatch(&found));
            Assert::IsFalse(found != 0, L"Turkish 'I' should NOT match 'i' case-insensitively");
        }

        TEST_METHOD(Utf16LE_Match)
        {
            wil::unique_bstr pattern(SysAllocString(L"world"));
            wil::com_ptr<IRegEx> regex;
            RepStrRegExCreate(pattern.get(), RegExSyntaxFlags_ECMAScript, 0, nullptr, regex.put());

            RegExString inputStr = MakeString(u"hello world"sv, RegExEncoding_utf16le);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->CreateMatchEnumerator(&inputStr, RegExMatchFlag_default, enumerator.put());

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
            wil::unique_bstr pattern(SysAllocString(L"a"));
            wil::com_ptr<IRegEx> regex;
            RepStrRegExCreate(pattern.get(), RegExSyntaxFlags_ECMAScript, 0, nullptr, regex.put());

            RegExString inputStr = MakeString("a"sv, RegExEncoding_utf8);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->CreateMatchEnumerator(&inputStr, RegExMatchFlag_default, enumerator.put());

            RegExEnumerationState state = {};
            enumerator->GetState(&state);
            Assert::AreEqual((int)RegExEnumerationState_not_started, (int)state);

            VARIANT_BOOL found = VARIANT_FALSE;
            enumerator->NextMatch(&found);
            enumerator->GetState(&state);
            Assert::AreEqual((int)RegExEnumerationState_enumerating, (int)state);

            enumerator->NextMatch(&found); // no more matches
            enumerator->GetState(&state);
            Assert::AreEqual((int)RegExEnumerationState_finished, (int)state);
        }

        TEST_METHOD(FormatReplacement_Utf8)
        {
            wil::unique_bstr pattern(SysAllocString(L"(\\w+)@(\\w+)"));
            wil::com_ptr<IRegEx> regex;
            RepStrRegExCreate(pattern.get(), RegExSyntaxFlags_ECMAScript, 0, nullptr, regex.put());

            RegExString inputStr = MakeString("user@host"sv, RegExEncoding_utf8);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->CreateMatchEnumerator(&inputStr, RegExMatchFlag_default, enumerator.put());

            VARIANT_BOOL found = VARIANT_FALSE;
            Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
            Assert::IsTrue(found != 0);

            wil::unique_bstr replacement(SysAllocString(L"$2@$1"));
            HRESULT hr = enumerator->SetFormatTemplate(replacement.get(), RegExFormatFlag_default);
            Assert::AreEqual(S_OK, hr);

            RegExString output = {};
            hr = enumerator->Format(RegExEncoding_utf8, &output);
            Assert::AreEqual(S_OK, hr);
            Assert::AreEqual(LONGLONG(9), output.size); // "host@user" = 9 bytes
            Assert::AreEqual((int)RegExEncoding_utf8, (int)output.encoding);

            Assert::AreEqual("host@user"sv, MakeView<char>(output));
        }
    };
}
