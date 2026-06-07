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
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
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
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
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
                    &inputBytes,
                    RegExEncoding_utf8,
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
                    &inputBytes,
                    RegExEncoding_utf8,
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
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
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
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
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
                    &inputBytes,
                    RegExEncoding_utf16le,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
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
                    &bytes,
                    RegExEncoding_utf16le,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
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
                    &inputBytes,
                    RegExEncoding_utf8,
                    static_cast<RegExMatchFlags>(boost::regex_constants::match_prev_avail),
                    formatTemplate.get(),
                    RegExFormatFlag_default,
                    output.put()));
        }

        TEST_METHOD(ReplaceTo_AllOccurrences)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"a 1 b 2 c 3"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                S_OK,
                regex->ReplaceTo(
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
                    RegExEncoding_utf8,
                    stream.get()));
            Assert::AreEqual("a # b # c #"sv, stream->View<char>());
        }

        TEST_METHOD(ReplaceTo_TranscodeOutput)
        {
            // UTF-8 input, UTF-16LE output.
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"a 1 b 2"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                S_OK,
                regex->ReplaceTo(
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
                    RegExEncoding_utf16le,
                    stream.get()));
            Assert::IsTrue(u"a # b #"sv == stream->View<char16_t>());
        }

        TEST_METHOD(ReplaceTo_FirstOnly)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString(u8"a 1 b 2"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                S_OK,
                regex->ReplaceTo(
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_first_only,
                    RegExEncoding_utf8,
                    stream.get()));
            Assert::AreEqual("a # b 2"sv, stream->View<char>());
        }

        TEST_METHOD(ReplaceTo_NullStream)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"y"));

            Assert::AreEqual(
                E_POINTER,
                regex->ReplaceTo(
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
                    RegExEncoding_utf8,
                    nullptr));
        }

        TEST_METHOD(ReplaceTo_InvalidOutputEncoding)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"y"));

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                E_INVALIDARG,
                regex->ReplaceTo(
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
                    static_cast<RegExEncoding>(9999),
                    stream.get()));
        }

        TEST_METHOD(ReplaceTo_RejectsMatchPrevAvail)
        {
            auto regex = MakeRegEx(L"x");

            RegExBytes inputBytes = MakeString(u8"x"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"y"));

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                E_INVALIDARG,
                regex->ReplaceTo(
                    &inputBytes,
                    RegExEncoding_utf8,
                    static_cast<RegExMatchFlags>(boost::regex_constants::match_prev_avail),
                    formatTemplate.get(),
                    RegExFormatFlag_default,
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
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
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
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
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
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_default,
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
                    &inputBytes,
                    RegExEncoding_utf8,
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
                    &inputBytes,
                    RegExEncoding_utf8,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_sed,
                    output.put()));
            Assert::AreEqual(L"world hello"sv, MakeView(output.get()));
        }
    };
}
