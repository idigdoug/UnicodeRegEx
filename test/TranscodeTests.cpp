#include "pch.h"
#include "RegExTestHelpers.h"

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

namespace RegExTests
{
    TEST_CLASS(TranscodeTests)
    {
    public:

        TEST_METHOD(Transcode_Utf8ToBstr)
        {
            RegExBytes inputBytes = MakeString(u8"hello"sv);
            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                GetLibrary()->Transcode(&inputBytes, RegExEncoding_utf8, output.put()));
            Assert::AreEqual(L"hello"sv, MakeView(output.get()));
        }

        TEST_METHOD(Transcode_Utf16le_FastPath)
        {
            // Input is already UTF-16LE, so Transcode takes the fast path.
            RegExBytes inputBytes = MakeString(u"hello"sv);
            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                GetLibrary()->Transcode(&inputBytes, RegExEncoding_utf16le, output.put()));
            Assert::AreEqual(L"hello"sv, MakeView(output.get()));
        }

        TEST_METHOD(Transcode_Utf16beToBstr)
        {
            // UTF-16BE bytes for "AB" = 0x00 0x41 0x00 0x42
            unsigned char bytes[] = { 0x00, 0x41, 0x00, 0x42 };
            RegExBytes inputBytes = {
                .data_ptr = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(bytes)),
                .size = static_cast<LONGLONG>(sizeof(bytes)),
            };

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                GetLibrary()->Transcode(&inputBytes, RegExEncoding_utf16be, output.put()));
            Assert::AreEqual(L"AB"sv, MakeView(output.get()));
        }

        TEST_METHOD(Transcode_Latin1ToBstr)
        {
            // Latin-1: byte 0xE9 = U+00E9 (é).
            unsigned char bytes[] = { 'a', 0xE9, 'z' };
            RegExBytes inputBytes = {
                .data_ptr = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(bytes)),
                .size = static_cast<LONGLONG>(sizeof(bytes)),
            };

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                GetLibrary()->Transcode(&inputBytes, RegExEncoding_latin1, output.put()));
            Assert::AreEqual(L"a\u00E9z"sv, MakeView(output.get()));
        }

        TEST_METHOD(Transcode_EmptyInput)
        {
            RegExBytes inputBytes = MakeString(u8""sv);
            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                GetLibrary()->Transcode(&inputBytes, RegExEncoding_utf8, output.put()));
            Assert::AreEqual(L""sv, MakeView(output.get()));
        }

        TEST_METHOD(Transcode_InvalidEncoding)
        {
            RegExBytes inputBytes = MakeString(u8"hello"sv);
            wil::unique_bstr output;
            Assert::AreEqual(
                E_INVALIDARG,
                GetLibrary()->Transcode(&inputBytes, static_cast<RegExEncoding>(9999), output.put()));
            Assert::IsNull(output.get());
        }

        TEST_METHOD(Transcode_OddSize_Utf16)
        {
            auto bytes = MakeString(u"AB"sv);
            bytes.size = 3; // Not a multiple of 2 = invalid for UTF-16.

            wil::unique_bstr output;
            Assert::AreEqual(
                E_INVALIDARG,
                GetLibrary()->Transcode(&bytes, RegExEncoding_utf16le, output.put()));
        }

        TEST_METHOD(TranscodeTo_Utf8ToUtf16le)
        {
            RegExBytes inputBytes = MakeString(u8"hello"sv);
            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                S_OK,
                GetLibrary()->TranscodeTo(&inputBytes, RegExEncoding_utf8, RegExEncoding_utf16le, stream.get()));
            Assert::IsTrue(u"hello"sv == stream->View<char16_t>());
        }

        TEST_METHOD(TranscodeTo_SameEncoding_FastPath)
        {
            // Same encoding takes the fast path (direct byte copy).
            RegExBytes inputBytes = MakeString(u8"hello"sv);
            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                S_OK,
                GetLibrary()->TranscodeTo(&inputBytes, RegExEncoding_utf8, RegExEncoding_utf8, stream.get()));
            Assert::AreEqual("hello"sv, stream->View<char>());
        }

        TEST_METHOD(TranscodeTo_Utf16leToUtf8)
        {
            RegExBytes inputBytes = MakeString(u"hello"sv);
            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                S_OK,
                GetLibrary()->TranscodeTo(&inputBytes, RegExEncoding_utf16le, RegExEncoding_utf8, stream.get()));
            Assert::AreEqual("hello"sv, stream->View<char>());
        }

        TEST_METHOD(TranscodeTo_Utf8ToLatin1)
        {
            // UTF-8 "aéz" -> Latin-1 should produce 'a', 0xE9, 'z'.
            RegExBytes inputBytes = MakeString(u8"a\u00E9z"sv);
            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                S_OK,
                GetLibrary()->TranscodeTo(&inputBytes, RegExEncoding_utf8, RegExEncoding_latin1, stream.get()));

            auto bytes = stream->Bytes();
            Assert::AreEqual(size_t(3), bytes.size());
            Assert::AreEqual(BYTE('a'), bytes[0]);
            Assert::AreEqual(BYTE(0xE9), bytes[1]);
            Assert::AreEqual(BYTE('z'), bytes[2]);
        }

        TEST_METHOD(TranscodeTo_EmptyInput)
        {
            RegExBytes inputBytes = MakeString(u8""sv);
            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                S_OK,
                GetLibrary()->TranscodeTo(&inputBytes, RegExEncoding_utf8, RegExEncoding_utf16le, stream.get()));
            Assert::AreEqual(size_t(0), stream->Bytes().size());
        }

        TEST_METHOD(TranscodeTo_NullStream)
        {
            RegExBytes inputBytes = MakeString(u8"hello"sv);
            Assert::AreEqual(
                E_POINTER,
                GetLibrary()->TranscodeTo(&inputBytes, RegExEncoding_utf8, RegExEncoding_utf16le, nullptr));
        }

        TEST_METHOD(TranscodeTo_InvalidInputEncoding)
        {
            RegExBytes inputBytes = MakeString(u8"hello"sv);
            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                E_INVALIDARG,
                GetLibrary()->TranscodeTo(
                    &inputBytes, static_cast<RegExEncoding>(9999), RegExEncoding_utf8, stream.get()));
        }

        TEST_METHOD(TranscodeTo_InvalidOutputEncoding)
        {
            RegExBytes inputBytes = MakeString(u8"hello"sv);
            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                E_INVALIDARG,
                GetLibrary()->TranscodeTo(
                    &inputBytes, RegExEncoding_utf8, static_cast<RegExEncoding>(9999), stream.get()));
        }

        TEST_METHOD(TranscodeTo_OddSize_Utf16)
        {
            auto bytes = MakeString(u"AB"sv);
            bytes.size = 3;

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                E_INVALIDARG,
                GetLibrary()->TranscodeTo(&bytes, RegExEncoding_utf16le, RegExEncoding_utf8, stream.get()));
        }

        TEST_METHOD(TranscodeTo_LargeOutput_ExceedsBufferCapacity)
        {
            // Force multiple flushes through the OutputSink's internal buffer
            // (default capacity is 128 char32_t).
            std::string large;
            large.resize(1000, 'X');
            RegExBytes inputBytes = MakeString(std::string_view(large));

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                S_OK,
                GetLibrary()->TranscodeTo(&inputBytes, RegExEncoding_utf8, RegExEncoding_utf16le, stream.get()));

            // Expect 1000 UTF-16 code units, each 2 bytes = 2000 bytes.
            auto bytes = stream->Bytes();
            Assert::AreEqual(size_t(2000), bytes.size());

            auto chars = stream->View<char16_t>();
            Assert::AreEqual(size_t(1000), chars.size());
            for (auto c : chars)
            {
                Assert::IsTrue(u'X' == c);
            }
        }

        TEST_METHOD(Transcode_LargeOutput_ExceedsBufferCapacity)
        {
            // Same as TranscodeTo_LargeOutput but to a BSTR (vector destination),
            // forcing OutputSink::Flush() to append to its internal vector multiple times.
            std::string large;
            large.resize(1000, 'X');
            RegExBytes inputBytes = MakeString(std::string_view(large));

            wil::unique_bstr output;
            Assert::AreEqual(
                S_OK,
                GetLibrary()->Transcode(&inputBytes, RegExEncoding_utf8, output.put()));

            auto view = MakeView(output.get());
            Assert::AreEqual(size_t(1000), view.size());
            for (auto c : view)
            {
                Assert::AreEqual(L'X', c);
            }
        }

        TEST_METHOD(TranscodeTo_LargeOutput_Latin1)
        {
            // Exercises the latin1 case in TranscodeBufferInPlace and the Flush path
            // for vector destination at large size.
            std::string large;
            large.resize(1000, 'A');
            RegExBytes inputBytes = MakeString(std::string_view(large));

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                S_OK,
                GetLibrary()->TranscodeTo(&inputBytes, RegExEncoding_utf8, RegExEncoding_latin1, stream.get()));

            // Latin-1 stays 1 byte per char.
            auto bytes = stream->Bytes();
            Assert::AreEqual(size_t(1000), bytes.size());
            Assert::AreEqual(BYTE('A'), bytes[0]);
            Assert::AreEqual(BYTE('A'), bytes[999]);
        }

        TEST_METHOD(TranscodeTo_Utf16beInputToLatin1)
        {
            // Exercises AppendBytes utf16be input path with a large buffer that forces
            // multiple flushes, while also using latin1 as the output (covers latin1
            // ConvertInPlace in TranscodeBufferInPlace).
            std::vector<char16_t> buf(1000, u'A');
            ByteSwap16(std::span(buf));
            RegExBytes inputBytes = {
                .data_ptr = static_cast<LONGLONG>(reinterpret_cast<UINT_PTR>(buf.data())),
                .size = static_cast<LONGLONG>(buf.size() * sizeof(char16_t)),
            };

            wil::com_ptr<TestMemoryStream> stream(new TestMemoryStream());
            Assert::AreEqual(
                S_OK,
                GetLibrary()->TranscodeTo(&inputBytes, RegExEncoding_utf16be, RegExEncoding_latin1, stream.get()));

            auto bytes = stream->Bytes();
            Assert::AreEqual(size_t(1000), bytes.size());
            Assert::AreEqual(BYTE('A'), bytes[0]);
        }
    };
}
