#include "pch.h"
#include "RegExTestHelpers.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

// Coverage tests for the Latin-1 and UTF-16BE input encodings. These exercise
// the template instantiations of Visit* in RegExMatchBase that are otherwise
// unreachable from the rest of the suite (which is dominated by UTF-8 and
// UTF-16LE).

namespace RegExTests
{
    TEST_CLASS(Latin1InputTests)
    {
    public:

        TEST_METHOD(Search_BasicMatch)
        {
            auto regex = MakeRegEx(L"world");

            // Latin-1: each byte maps directly to a code point.
            RegExBytes inputBytes = MakeString("hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(inputBytes, RegExCodePage_latin1, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            Assert::AreEqual(S_OK, results->GetSubMatch(0, &sub));
            Assert::AreEqual(LONGLONG(6), sub.offset);
            Assert::AreEqual(LONGLONG(5), sub.size);
        }

        TEST_METHOD(Search_HighByteMatches)
        {
            // Latin-1: byte 0xE9 = U+00E9 (é). Regex matches the literal code point.
            auto regex = MakeRegEx(L"\u00E9");

            char input[] = { 'a', '\xE9', 'b' };
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(input)),
                .size = sizeof(input),
            };

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(inputBytes, RegExCodePage_latin1, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(1), sub.offset);
            Assert::AreEqual(LONGLONG(1), sub.size);
        }

        TEST_METHOD(Match_WholeString)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString("12345"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(inputBytes, RegExCodePage_latin1, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());
        }

        TEST_METHOD(EnumerateMatches_Multiple)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString("a 1 b 22 c 333"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            Assert::AreEqual(
                S_OK,
                regex->EnumerateMatches(inputBytes, RegExCodePage_latin1, 0, RegExMatchFlag_default, enumerator.put()));

            VARIANT_BOOL found = VARIANT_FALSE;
            int count = 0;
            while (count < 10)
            {
                Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
                if (!found) break;
                count++;
            }
            Assert::AreEqual(3, count);
        }

        TEST_METHOD(Format_BackReferences)
        {
            auto regex = MakeRegEx(L"(\\w+) (\\w+)");

            RegExBytes inputBytes = MakeString("hello world"sv);

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(inputBytes, RegExCodePage_latin1, 0, RegExMatchFlag_default, enumerator.put());

            VARIANT_BOOL found = VARIANT_FALSE;
            enumerator->NextMatch(&found);
            Assert::IsTrue(found != 0);

            wil::unique_bstr formatTemplate(SysAllocString(L"$2 $1"));
            enumerator->SetFormatTemplate(formatTemplate.get(), RegExFormatFlag_perl);

            wil::unique_bstr output;
            Assert::AreEqual(S_OK, enumerator->Format(output.put()));
            Assert::AreEqual(L"world hello"sv, MakeView(output.get()));
        }

        TEST_METHOD(CopyInput_ToBstr)
        {
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString("hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExCodePage_latin1, 0, RegExMatchFlag_default, results.put());

            wil::unique_bstr output;
            Assert::AreEqual(S_OK, results->CopyInput(0, 5, output.put()));
            Assert::AreEqual(L"hello"sv, MakeView(output.get()));
        }

        TEST_METHOD(CopyInputTo_SameCodePage_FastPath)
        {
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString("hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExCodePage_latin1, 0, RegExMatchFlag_default, results.put());

            auto stream = MakeMemoryStream();
            Assert::AreEqual(S_OK, results->CopyInputTo(6, 5, stream.get(), RegExCodePage_latin1));
            Assert::AreEqual("world"sv, StreamView(stream.get()));
        }

        TEST_METHOD(CopyInputTo_Transcode)
        {
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString("hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExCodePage_latin1, 0, RegExMatchFlag_default, results.put());

            auto stream = MakeMemoryStream();
            Assert::AreEqual(S_OK, results->CopyInputTo(0, 5, stream.get(), RegExCodePage_utf16le));
            Assert::IsTrue(u"hello"sv == StreamView<char16_t>(stream.get()));
        }

        TEST_METHOD(InputCodePage_Property)
        {
            auto regex = MakeRegEx(L"h");

            RegExBytes inputBytes = MakeString("hello"sv);

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExCodePage_latin1, 0, RegExMatchFlag_default, results.put());

            UINT32 codePage = RegExCodePage_none;
            Assert::AreEqual(S_OK, results->get_InputCodePage(&codePage));
            Assert::AreEqual((int)RegExCodePage_latin1, (int)codePage);
        }

        TEST_METHOD(Replace_Default)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString("a 1 b 2"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExCodePage_latin1,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"a # b #"sv, MakeView(output.get()));
        }

        TEST_METHOD(ReplaceTo_Default)
        {
            auto regex = MakeRegEx(L"\\d+");

            RegExBytes inputBytes = MakeString("a 1 b 2"sv);
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            auto stream = MakeMemoryStream();
            Assert::AreEqual(
                S_OK,
                regex->ReplaceTo(
                    inputBytes,
                    RegExCodePage_latin1,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    stream.get(),
                    RegExCodePage_latin1));
            Assert::AreEqual("a # b #"sv, StreamView(stream.get()));
        }

        TEST_METHOD(StartByteOffset_LookBehind)
        {
            // Latin-1 with non-zero startByteOffset; exercises FromSpanAndByteOffset for latin1.
            auto regex = MakeRegEx(L"(?<=hello )world");

            RegExBytes inputBytes = MakeString("hello world"sv);

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(inputBytes, RegExCodePage_latin1, 6, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());
        }
    };

    TEST_CLASS(Utf16BEInputTests)
    {
    public:

        TEST_METHOD(Search_BasicMatch)
        {
            auto regex = MakeRegEx(L"world");

            char16_t buf[] = u"hello world";
            ByteSwap16(std::span(buf, std::size(buf) - 1));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>((std::size(buf) - 1) * sizeof(char16_t)),
            };

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(inputBytes, RegExCodePage_utf16be, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(12), sub.offset); // 6 chars * 2 bytes
            Assert::AreEqual(LONGLONG(10), sub.size);          // 5 chars * 2 bytes
        }

        TEST_METHOD(Search_SurrogatePair)
        {
            // U+1F600 (😀) regex against UTF-16BE surrogate pair.
            auto regex = MakeRegEx(L"\U0001F600");

            char16_t buf[] = u"A\U0001F600B";
            auto const count = std::size(buf) - 1; // exclude null
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Search(inputBytes, RegExCodePage_utf16be, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());

            RegExSubMatch sub = {};
            results->GetSubMatch(0, &sub);
            Assert::AreEqual(LONGLONG(2), sub.offset); // skip 'A' (2 bytes)
            Assert::AreEqual(LONGLONG(4), sub.size);         // surrogate pair (4 bytes)
        }

        TEST_METHOD(Match_WholeString)
        {
            auto regex = MakeRegEx(L"\\d+");

            char16_t buf[] = u"12345";
            auto const count = std::size(buf) - 1;
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };

            wil::com_ptr<IRegExMatchResults> results;
            Assert::AreEqual(
                S_OK,
                regex->Match(inputBytes, RegExCodePage_utf16be, 0, RegExMatchFlag_default, results.put()));
            Assert::IsNotNull(results.get());
        }

        TEST_METHOD(EnumerateMatches_Multiple)
        {
            auto regex = MakeRegEx(L"\\d+");

            char16_t buf[] = u"a 1 b 22 c 333";
            auto const count = std::size(buf) - 1;
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            Assert::AreEqual(
                S_OK,
                regex->EnumerateMatches(inputBytes, RegExCodePage_utf16be, 0, RegExMatchFlag_default, enumerator.put()));

            VARIANT_BOOL found = VARIANT_FALSE;
            int matchCount = 0;
            while (matchCount < 10)
            {
                Assert::AreEqual(S_OK, enumerator->NextMatch(&found));
                if (!found) break;
                matchCount++;
            }
            Assert::AreEqual(3, matchCount);
        }

        TEST_METHOD(Format_BackReferences)
        {
            auto regex = MakeRegEx(L"(\\w+) (\\w+)");

            char16_t buf[] = u"hello world";
            auto const count = std::size(buf) - 1;
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            regex->EnumerateMatches(inputBytes, RegExCodePage_utf16be, 0, RegExMatchFlag_default, enumerator.put());

            VARIANT_BOOL found = VARIANT_FALSE;
            enumerator->NextMatch(&found);

            wil::unique_bstr formatTemplate(SysAllocString(L"$2 $1"));
            enumerator->SetFormatTemplate(formatTemplate.get(), RegExFormatFlag_perl);

            wil::unique_bstr output;
            Assert::AreEqual(S_OK, enumerator->Format(output.put()));
            Assert::AreEqual(L"world hello"sv, MakeView(output.get()));
        }

        TEST_METHOD(CopyInput_ToBstr)
        {
            auto regex = MakeRegEx(L"h");

            char16_t buf[] = u"hello";
            auto const count = std::size(buf) - 1;
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExCodePage_utf16be, 0, RegExMatchFlag_default, results.put());

            wil::unique_bstr output;
            Assert::AreEqual(S_OK, results->CopyInput(0, 10, output.put()));
            Assert::AreEqual(L"hello"sv, MakeView(output.get()));
        }

        TEST_METHOD(CopyInputTo_SameCodePage_FastPath)
        {
            auto regex = MakeRegEx(L"h");

            char16_t buf[] = u"hello world";
            auto const count = std::size(buf) - 1;
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExCodePage_utf16be, 0, RegExMatchFlag_default, results.put());

            auto stream = MakeMemoryStream();
            // Copy bytes 12..22 = "world" in BE. Same code page = byte-for-byte copy.
            Assert::AreEqual(S_OK, results->CopyInputTo(12, 10, stream.get(), RegExCodePage_utf16be));
            // Verify bytes match the BE source.
            auto written = StreamBytes(stream.get());
            Assert::AreEqual(size_t(10), written.size());
            auto const* pSource = reinterpret_cast<BYTE const*>(buf) + 12;
            for (size_t i = 0; i < 10; ++i)
            {
                Assert::AreEqual(pSource[i], written[i]);
            }
        }

        TEST_METHOD(CopyInputTo_Transcode)
        {
            auto regex = MakeRegEx(L"h");

            char16_t buf[] = u"hello";
            auto const count = std::size(buf) - 1;
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExCodePage_utf16be, 0, RegExMatchFlag_default, results.put());

            auto stream = MakeMemoryStream();
            Assert::AreEqual(S_OK, results->CopyInputTo(0, 10, stream.get(), RegExCodePage_utf8));
            Assert::AreEqual("hello"sv, StreamView(stream.get()));
        }

        TEST_METHOD(InputCodePage_Property)
        {
            auto regex = MakeRegEx(L"h");

            char16_t buf[] = u"hello";
            auto const count = std::size(buf) - 1;
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };

            wil::com_ptr<IRegExMatchResults> results;
            regex->Search(inputBytes, RegExCodePage_utf16be, 0, RegExMatchFlag_default, results.put());

            UINT32 codePage = RegExCodePage_none;
            Assert::AreEqual(S_OK, results->get_InputCodePage(&codePage));
            Assert::AreEqual((int)RegExCodePage_utf16be, (int)codePage);
        }

        TEST_METHOD(Replace_Default)
        {
            auto regex = MakeRegEx(L"\\d+");

            char16_t buf[] = u"a 1 b 2";
            auto const count = std::size(buf) - 1;
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                regex->Replace(
                    inputBytes,
                    RegExCodePage_utf16be,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    output.put()));
            Assert::AreEqual(L"a # b #"sv, MakeView(output.get()));
        }

        TEST_METHOD(ReplaceTo_Default)
        {
            auto regex = MakeRegEx(L"\\d+");

            char16_t buf[] = u"a 1 b 2";
            auto const count = std::size(buf) - 1;
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };
            wil::unique_bstr formatTemplate(SysAllocString(L"#"));

            auto stream = MakeMemoryStream();
            Assert::AreEqual(
                S_OK,
                regex->ReplaceTo(
                    inputBytes,
                    RegExCodePage_utf16be,
                    0,
                    RegExMatchFlag_default,
                    formatTemplate.get(),
                    RegExFormatFlag_perl,
                    stream.get(),
                    RegExCodePage_utf8));
            Assert::AreEqual("a # b #"sv, StreamView(stream.get()));
        }

        TEST_METHOD(StartByteOffset_InvalidMidSequence)
        {
            // Offset 2 into "😀" surrogate pair targets the low surrogate; reject.
            auto regex = MakeRegEx(L"x");

            char16_t buf[] = u"\U0001F600";
            auto const count = std::size(buf) - 1; // 2 char16_t for surrogate pair
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };

            wil::com_ptr<IRegExMatchEnumerator> enumerator;
            Assert::AreEqual(
                E_INVALIDARG,
                regex->EnumerateMatches(inputBytes, RegExCodePage_utf16be, 2, RegExMatchFlag_default, enumerator.put()));
        }

        TEST_METHOD(Transcode_Utf16beToBstr)
        {
            char16_t buf[] = u"hello";
            auto const count = std::size(buf) - 1;
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                GetLibrary()->Transcode(inputBytes, RegExCodePage_utf16be, output.put()));
            Assert::AreEqual(L"hello"sv, MakeView(output.get()));
        }

        TEST_METHOD(TranscodeTo_Utf16beToLatin1)
        {
            // Pull in the Utf16BE ConvertInPlace via the OutputSink default-case path
            // by selecting Utf16BE as the output code page too.
            char16_t buf[] = u"hi";
            auto const count = std::size(buf) - 1;
            ByteSwap16(std::span(buf, count));
            RegExBytes inputBytes = {
                .data = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf)),
                .size = static_cast<LONGLONG>(count * sizeof(char16_t)),
            };

            auto stream = MakeMemoryStream();
            Assert::AreEqual(
                S_OK,
                GetLibrary()->TranscodeTo(inputBytes, RegExCodePage_utf16be, stream.get(), RegExCodePage_utf16be));
            // Fast-path: bytes are copied verbatim.
            auto written = StreamBytes(stream.get());
            Assert::AreEqual(size_t(4), written.size());
        }

        TEST_METHOD(TranscodeTo_Utf8ToUtf16be)
        {
            // Exercises OutputSink's Utf16BE ConvertInPlace path.
            RegExBytes inputBytes = MakeString(u8"hi"sv);

            auto stream = MakeMemoryStream();
            Assert::AreEqual(
                S_OK,
                GetLibrary()->TranscodeTo(inputBytes, RegExCodePage_utf8, stream.get(), RegExCodePage_utf16be));

            // Expect BE bytes: 0x00 0x68 0x00 0x69 for "hi".
            auto bytes = StreamBytes(stream.get());
            Assert::AreEqual(size_t(4), bytes.size());
            Assert::AreEqual(BYTE(0x00), bytes[0]);
            Assert::AreEqual(BYTE('h'), bytes[1]);
            Assert::AreEqual(BYTE(0x00), bytes[2]);
            Assert::AreEqual(BYTE('i'), bytes[3]);
        }
    };
}
