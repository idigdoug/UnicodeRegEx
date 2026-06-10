#include "pch.h"
#include "RegExTestHelpers.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

namespace RegExTests
{
    TEST_CLASS(ReplaceTests)
    {
    public:

        TEST_METHOD(Replace_AllOccurrences_Default)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"abc 123 def 456 ghi"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"abc # def # ghi"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_BackReferences)
        {
            // Swap the two captured groups.
            auto regex = MakeRegEx(L"(\\w+) (\\w+)");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"$2 $1"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"world hello"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_FirstOnly)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"a 1 b 2 c 3"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_first_only,
                    output.put()));
            Assert::AreEqual(L"a # b 2 c 3"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_NoCopy)
        {
            // format_no_copy = output only the replacements, not the surrounding text.
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"a 1 b 22 c 333"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"[$&]"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_no_copy,
                    output.put()));
            Assert::AreEqual(L"[1][22][333]"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_NoMatches)
        {
            // No matches means the output equals the input.
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"hello world"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_EmptyInput)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8""sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L""sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_Utf16Input)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u"a 1 b 2"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf16le,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"a # b #"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_InvalidInputAlignment_Utf16)
        {
            auto regex = MakeRegEx(L"x");

            // Construct a RegExBytes with an odd size to fail alignment check.
            auto bytes = MakeString(u"AB"sv);
            bytes.size = 3; // Not a multiple of 2.

            wil::unique_bstr formatTemplate(SysAllocString(L"y"));
            wil::unique_bstr output;
            Assert::AreEqual(
                E_INVALIDARG,
                regex->Replace(
                    bytes,
                    RegExEncoding_utf16le,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
        }

        TEST_METHOD(Replace_RejectsMatchPrevAvail)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"y"));

            wil::unique_bstr output;
            Assert::AreEqual(
                E_INVALIDARG,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    static_cast<RegExMatchFlags>(boost::regex_constants::match_prev_avail),
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
        }

        TEST_METHOD(ReplaceTo_AllOccurrences)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"a 1 b 2 c 3"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            auto stream = MakeMemoryStream();
            Assert::AreEqual(
                S_OK,
                regex->ReplaceTo(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    RegExEncoding_utf8,
                    stream.get()));
            Assert::AreEqual("a # b # c #"sv, StreamView(stream.get()));
        }

        TEST_METHOD(ReplaceTo_TranscodeOutput)
        {
            // UTF-8 input, UTF-16LE output.
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"a 1 b 2"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            auto stream = MakeMemoryStream();
            Assert::AreEqual(
                S_OK,
                regex->ReplaceTo(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    RegExEncoding_utf16le,
                    stream.get()));
            Assert::IsTrue(u"a # b #"sv == StreamView<char16_t>(stream.get()));
        }

        TEST_METHOD(ReplaceTo_FirstOnly)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"a 1 b 2"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            auto stream = MakeMemoryStream();
            Assert::AreEqual(
                S_OK,
                regex->ReplaceTo(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_first_only,
                    RegExEncoding_utf8,
                    stream.get()));
            Assert::AreEqual("a # b 2"sv, StreamView(stream.get()));
        }

        TEST_METHOD(ReplaceTo_NullStream)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"y"));

            Assert::AreEqual(
                E_POINTER,
                regex->ReplaceTo(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    RegExEncoding_utf8,
                    nullptr));
        }

        TEST_METHOD(ReplaceTo_InvalidOutputEncoding)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"y"));

            auto stream = MakeMemoryStream();
            Assert::AreEqual(
                E_INVALIDARG,
                regex->ReplaceTo(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    static_cast<RegExEncoding>(9999),
                    stream.get()));
        }

        TEST_METHOD(ReplaceTo_RejectsMatchPrevAvail)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"y"));

            auto stream = MakeMemoryStream();
            Assert::AreEqual(
                E_INVALIDARG,
                regex->ReplaceTo(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    static_cast<RegExMatchFlags>(boost::regex_constants::match_prev_avail),
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    RegExEncoding_utf8,
                    stream.get()));
        }

        // ----- Replacement transformations: \U, \L, \E -----

        TEST_METHOD(Replace_UppercaseTransform)
        {
            // \U starts uppercasing; \E ends a transform region.
            auto regex = MakeRegEx(L"(\\w+)");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"\\U$1\\E"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"HELLO WORLD"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_LowercaseTransform)
        {
            auto regex = MakeRegEx(L"(\\w+)");

            RegExBytes inputBytes = MakeString(u8"HELLO WORLD"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"\\L$1\\E"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"hello world"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_UppercaseEndsWithE)
        {
            // \E in the middle ends the transformation so only the first portion is uppercased.
            auto regex = MakeRegEx(L"(\\w+) (\\w+)");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"\\U$1\\E $2"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"HELLO world"sv, MakeView(output.get()));
        }

        // ----- Sed-style replacements -----

        TEST_METHOD(Replace_SedStyle_AmpersandIsWholeMatch)
        {
            // Under format_sed, & is the whole match (like Perl $&).
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"x 123 y"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"[&]"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_sed,
                    output.put()));
            Assert::AreEqual(L"x [123] y"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_SedStyle_BackslashBackref)
        {
            // Under format_sed, \1 is backref to group 1.
            auto regex = MakeRegEx(L"(\\w+) (\\w+)");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"\\2 \\1"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_sed,
                    output.put()));
            Assert::AreEqual(L"world hello"sv, MakeView(output.get()));
        }

        // ----- startByteOffset -----

        TEST_METHOD(Replace_StartByteOffset_PrefixCopiedUnchanged)
        {
            // Bytes before startByteOffset are copied verbatim; only the suffix is searched.
            // Input "123 abc 456 abc". Starting at byte 4 means the leading "123 " is
            // outside the search region but should still appear in the output unchanged.
            auto regex = MakeRegEx(L"abc");

            RegExBytes inputBytes = MakeString(u8"123 abc 456 abc"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"X"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    4,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"123 X 456 X"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_StartByteOffset_SkipsEarlierMatch)
        {
            // The first "abc" at offset 0 is not searched (startByteOffset=4 is past it).
            auto regex = MakeRegEx(L"abc");

            RegExBytes inputBytes = MakeString(u8"abc xyz abc"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"X"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    4,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            // Leading "abc " is copied unchanged because it's before the offset.
            Assert::AreEqual(L"abc xyz X"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_StartByteOffset_AtEnd)
        {
            // Starting at end-of-input should produce a copy of the input with no replacements.
            auto regex = MakeRegEx(L"abc");

            RegExBytes inputBytes = MakeString(u8"abc abc"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"X"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    7,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"abc abc"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_StartByteOffset_PastEnd_Fails)
        {
            auto regex = MakeRegEx(L"abc");

            RegExBytes inputBytes = MakeString(u8"abc"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"X"));

            wil::unique_bstr output;
            Assert::AreEqual(
                E_INVALIDARG,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    4,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
        }

        TEST_METHOD(Replace_StartByteOffset_LookBehindUsesPrefix)
        {
            // (?<=hello )world should match "world" only when preceded by "hello ".
            // With startByteOffset=6 (the 'w'), the lookbehind sees "hello " in the
            // pre-offset region, so the match still succeeds.
            auto regex = MakeRegEx(L"(?<=hello )world");

            RegExBytes inputBytes = MakeString(u8"hello world"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"earth"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    6,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"hello earth"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_StartByteOffset_NoCopy_DropsPrefix)
        {
            // With format_no_copy, bytes before the offset are not copied (consistent
            // with the "no_copy" semantics: only matched-and-formatted text is emitted).
            auto regex = MakeRegEx(L"abc");

            RegExBytes inputBytes = MakeString(u8"123 abc 456 abc"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"X"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    4,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_no_copy,
                    output.put()));
            Assert::AreEqual(L"XX"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_StartByteOffset_Utf16)
        {
            // Verify that byte offset is interpreted in input encoding units.
            // Input "abc abc" in UTF-16LE = 14 bytes; offset 8 lands on the second "abc".
            auto regex = MakeRegEx(L"abc");

            RegExBytes inputBytes = MakeString(u"abc abc"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"X"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf16le,
                    8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"abc X"sv, MakeView(output.get()));
        }

        TEST_METHOD(Replace_StartByteOffset_InvalidMidSequence_Utf8)
        {
            // Multi-byte UTF-8 character; offset that lands inside a sequence should fail.
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"\u00e9"sv); // 0xC3 0xA9
            wil::unique_bstr formatTemplate(SysAllocString(L"y"));

            wil::unique_bstr output;
            Assert::AreEqual(
                E_INVALIDARG,
                regex->Replace(
                    inputBytes,
                    RegExEncoding_utf8,
                    1,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
        }
    };
}
