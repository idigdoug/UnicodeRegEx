#include "pch.h"
#include "resource.h"
#include <utf.h>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;
using namespace std::string_view_literals;

namespace UtfTests
{
    // Helpers

    static std::u16string_view
    ByteSwap16(std::span<char16_t> be)
    {
        for (auto& ch : be)
        {
            ch = _byteswap_ushort(ch);
        }
        return std::u16string_view(be.data(), be.size());
    }

    template<class CH>
    static std::basic_string_view<CH>
    S2V(std::span<CH> span) noexcept
    {
        return std::basic_string_view<CH>(span.data(), span.size());
    }

    template<class IteratorT>
    static void
    VerifyForwardBackwardConsistency(IteratorT begin, IteratorT end)
    {
        std::vector<IteratorT> positions;
        for (auto it = begin; it != end; ++it)
        {
            positions.push_back(it);
        }

        auto rit = end;
        for (size_t i = positions.size(); i != 0; i -= 1)
        {
            --rit;
            Assert::IsTrue(rit == positions[i - 1],
                L"Backward iteration did not reach the same position as forward iteration.");
        }
    }

    template<class IteratorT>
    static std::vector<char32_t>
    CollectCodePoints(IteratorT begin, IteratorT end)
    {
        std::vector<char32_t> result;
        for (auto it = begin; it != end; ++it)
        {
            result.push_back(*it);
        }
        return result;
    }

    template<class CharT>
    static std::basic_string_view<CharT>
    VectorToView(const std::vector<CharT>& vec)
    {
        return std::basic_string_view<CharT>(vec.data(), vec.size());
    }

    static std::span<char8_t const>
    LoadTortureTestData()
    {
        HMODULE hModule = nullptr;
        GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCWSTR>(&CollectCodePoints<utf8::CodePointIterator>), &hModule);
        HRSRC hRes = FindResourceW(hModule, MAKEINTRESOURCEW(IDR_UTF8_TORTURE_TEST), RT_RCDATA);
        Assert::IsNotNull(hRes, L"UTF8-torture-test.txt resource not found.");
        HGLOBAL hData = LoadResource(hModule, hRes);
        Assert::IsNotNull(hData);
        auto* ptr = static_cast<char8_t const*>(LockResource(hData));
        auto size = SizeofResource(hModule, hRes) / sizeof(char8_t);
        return { ptr, size };
    }

    // ==================== utf struct tests ====================

    TEST_CLASS(UtfHelperTests)
    {
    public:

        TEST_METHOD(IsAscii)
        {
            Assert::IsTrue(utf::IsAscii(0));
            Assert::IsTrue(utf::IsAscii(0x7F));
            Assert::IsFalse(utf::IsAscii(0x80));
            Assert::IsFalse(utf::IsAscii(0xFFFF));
        }

        TEST_METHOD(IsLatin1)
        {
            Assert::IsTrue(utf::IsLatin1(0));
            Assert::IsTrue(utf::IsLatin1(0xFF));
            Assert::IsFalse(utf::IsLatin1(0x100));
        }

        TEST_METHOD(IsBmp)
        {
            Assert::IsTrue(utf::IsBmp(0));
            Assert::IsTrue(utf::IsBmp(0xFFFF));
            Assert::IsFalse(utf::IsBmp(0x10000));
        }

        TEST_METHOD(IsCodePoint)
        {
            Assert::IsTrue(utf::IsCodePoint(0));
            Assert::IsTrue(utf::IsCodePoint(0x10FFFF));
            Assert::IsFalse(utf::IsCodePoint(0x110000));
        }

        TEST_METHOD(IsHighSurrogate)
        {
            Assert::IsFalse(utf::IsHighSurrogate(0));
            Assert::IsFalse(utf::IsHighSurrogate(0xD7FF));
            Assert::IsTrue(utf::IsHighSurrogate(0xD800));
            Assert::IsTrue(utf::IsHighSurrogate(0xDBFF));
            Assert::IsFalse(utf::IsHighSurrogate(0xDC00));
            Assert::IsFalse(utf::IsHighSurrogate(0x1D800));
        }

        TEST_METHOD(IsLowSurrogate)
        {
            Assert::IsFalse(utf::IsLowSurrogate(0));
            Assert::IsFalse(utf::IsLowSurrogate(0xDBFF));
            Assert::IsTrue(utf::IsLowSurrogate(0xDC00));
            Assert::IsTrue(utf::IsLowSurrogate(0xDFFF));
            Assert::IsFalse(utf::IsLowSurrogate(0xE000));
            Assert::IsFalse(utf::IsLowSurrogate(0x1DC00));
        }

        TEST_METHOD(FromSurrogatePair)
        {
            // U+1F600 = D83D DE00
            Assert::AreEqual(char32_t(0x1F600), utf::FromSurrogatePair(0xD83D, 0xDE00));
            // U+10000 = D800 DC00
            Assert::AreEqual(char32_t(0x10000), utf::FromSurrogatePair(0xD800, 0xDC00));
            // U+10FFFF = DBFF DFFF
            Assert::AreEqual(char32_t(0x10FFFF), utf::FromSurrogatePair(0xDBFF, 0xDFFF));
        }
    };

    // ==================== latin1 tests ====================

    TEST_CLASS(Latin1Tests)
    {
    public:

        // --- Encode ---

        TEST_METHOD(Encode_Ascii)
        {
            char dst[1];
            Assert::AreEqual(1u, latin1::Encode(dst, U'A'));
            Assert::AreEqual('A', dst[0]);
        }

        TEST_METHOD(Encode_Latin1Char)
        {
            char dst[1];
            Assert::AreEqual(1u, latin1::Encode(dst, 0xE9)); // é
            Assert::AreEqual('\xE9', dst[0]);
        }

        TEST_METHOD(Encode_OutOfRange)
        {
            char dst[1];
            Assert::AreEqual(1u, latin1::Encode(dst, 0x100)); // above latin1
            Assert::AreEqual('?', dst[0]);
        }

        // --- ConvertInPlace ---

        TEST_METHOD(ConvertInPlace_Ascii)
        {
            std::u32string buf{ U"Hello"sv };
            auto result = S2V(latin1::ConvertInPlace(buf));
            Assert::AreEqual("Hello"sv, result);
        }

        TEST_METHOD(ConvertInPlace_Latin1Range)
        {
            std::u32string buf{ U"éÿ"sv };
            auto result = S2V(latin1::ConvertInPlace(buf));
            Assert::AreEqual("\xE9\xFF"sv, result);
        }

        TEST_METHOD(ConvertInPlace_OutOfRange)
        {
            std::u32string buf{ U"Ā€😀"sv };
            auto result = S2V(latin1::ConvertInPlace(buf));
            Assert::AreEqual("???"sv, result);
        }

        TEST_METHOD(ConvertInPlace_Empty)
        {
            std::u32string buf;
            auto result = S2V(latin1::ConvertInPlace(buf));
            Assert::IsTrue(result.empty());
        }

        // --- CodePointIterator ---

        TEST_METHOD(Iterator_BasicAscii)
        {
            auto data = "Hello"sv;
            auto [begin, end] = latin1::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"Hello"sv);
        }

        TEST_METHOD(Iterator_HighBytes)
        {
            auto data = "\xE9\xFF\x00"sv;
            auto [begin, end] = latin1::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"\u00E9\u00FF\0"sv);
        }

        TEST_METHOD(Iterator_ForwardBackwardConsistency)
        {
            auto data = "abcdefghij"sv;
            auto [begin, end] = latin1::CodePointIterator::FromSpan(data);
            VerifyForwardBackwardConsistency(begin, end);
        }

        TEST_METHOD(Iterator_RandomAccess)
        {
            auto data = "ABCDE"sv;
            auto [begin, end] = latin1::CodePointIterator::FromSpan(data);
            Assert::AreEqual(char32_t('C'), begin[2]);
            Assert::AreEqual(char32_t('E'), begin[4]);
            auto mid = begin + 3;
            Assert::AreEqual(char32_t('D'), *mid);
            Assert::AreEqual(ptrdiff_t(3), mid - begin);
        }

        TEST_METHOD(Iterator_Empty)
        {
            auto data = ""sv;
            auto [begin, end] = latin1::CodePointIterator::FromSpan(data);
            Assert::IsTrue(begin == end);
        }

        TEST_METHOD(Iterator_ByteOffset)
        {
            auto data = "ABCDE"sv;
            auto [begin, end] = latin1::CodePointIterator::FromSpan(data);
            Assert::AreEqual(size_t(0), begin.ByteOffset(data.data()));
            auto mid = begin + 3;
            Assert::AreEqual(size_t(3), mid.ByteOffset(data.data()));
            Assert::AreEqual(size_t(5), end.ByteOffset(data.data()));
        }

        TEST_METHOD(FromSpanAndByteOffset_ValidOffsets)
        {
            auto data = "ABCDE"sv;
            for (size_t i = 0; i <= data.size(); i += 1)
            {
                auto [pos, begin, end] = latin1::CodePointIterator::FromSpanAndByteOffset(data, i);
                Assert::IsTrue(pos == begin + static_cast<ptrdiff_t>(i));
            }
        }

        TEST_METHOD(FromSpanAndByteOffset_PastEnd)
        {
            auto data = "ABCDE"sv;
            auto [pos, begin, end] = latin1::CodePointIterator::FromSpanAndByteOffset(data, 6);
            Assert::IsTrue(pos == latin1::CodePointIterator());
        }

        TEST_METHOD(FromSpanAndByteOffset_Empty)
        {
            auto data = ""sv;
            auto [pos, begin, end] = latin1::CodePointIterator::FromSpanAndByteOffset(data, 0);
            Assert::IsTrue(pos == begin);
            Assert::IsTrue(begin == end);
        }
    };

    // ==================== utf8 tests ====================

    TEST_CLASS(Utf8Tests)
    {
    public:

        // --- Encode ---

        TEST_METHOD(Encode_Ascii)
        {
            char8_t dst[4];
            Assert::AreEqual(1u, utf8::Encode(dst, U'A'));
            Assert::IsTrue(dst[0] == u8'A');
        }

        TEST_METHOD(Encode_TwoByte)
        {
            char8_t dst[4];
            unsigned n = utf8::Encode(dst, 0xE9); // é
            Assert::AreEqual(2u, n);
            Assert::IsTrue(dst[0] == 0xC3);
            Assert::IsTrue(dst[1] == 0xA9);
        }

        TEST_METHOD(Encode_ThreeByte)
        {
            char8_t dst[4];
            unsigned n = utf8::Encode(dst, 0x20AC); // €
            Assert::AreEqual(3u, n);
            Assert::IsTrue(dst[0] == 0xE2);
            Assert::IsTrue(dst[1] == 0x82);
            Assert::IsTrue(dst[2] == 0xAC);
        }

        TEST_METHOD(Encode_FourByte)
        {
            char8_t dst[4];
            unsigned n = utf8::Encode(dst, 0x1F600); // 😀
            Assert::AreEqual(4u, n);
            Assert::IsTrue(dst[0] == 0xF0);
            Assert::IsTrue(dst[1] == 0x9F);
            Assert::IsTrue(dst[2] == 0x98);
            Assert::IsTrue(dst[3] == 0x80);
        }

        TEST_METHOD(Encode_InvalidCodePoint)
        {
            char8_t dst[4];
            unsigned n = utf8::Encode(dst, 0x110000); // above max
            Assert::AreEqual(3u, n); // replacement char U+FFFD
            Assert::IsTrue(dst[0] == 0xEF);
            Assert::IsTrue(dst[1] == 0xBF);
            Assert::IsTrue(dst[2] == 0xBD);
        }

        // --- ConvertInPlace ---

        TEST_METHOD(ConvertInPlace_Ascii)
        {
            std::u32string buf{ U"ABC"sv };
            auto result = S2V(utf8::ConvertInPlace(buf));
            Assert::IsTrue(result == u8"ABC"sv);
        }

        TEST_METHOD(ConvertInPlace_TwoByte)
        {
            std::u32string buf{ U"é"sv };
            auto result = S2V(utf8::ConvertInPlace(buf));
            Assert::IsTrue(result == u8"é"sv);
        }

        TEST_METHOD(ConvertInPlace_ThreeByte)
        {
            std::u32string buf{ U"€"sv };
            auto result = S2V(utf8::ConvertInPlace(buf));
            Assert::IsTrue(result == u8"€"sv);
        }

        TEST_METHOD(ConvertInPlace_FourByte)
        {
            std::u32string buf{ U"😀"sv };
            auto result = S2V(utf8::ConvertInPlace(buf));
            Assert::IsTrue(result == u8"😀"sv);
        }

        TEST_METHOD(ConvertInPlace_InvalidCodePoint)
        {
            std::u32string buf = { 0x110000 };
            auto result = S2V(utf8::ConvertInPlace(buf));
            Assert::IsTrue(result == u8"\uFFFD"sv);
        }

        TEST_METHOD(ConvertInPlace_RoundTrip)
        {
            std::u32string buf{ U"Aé€😀"sv };
            std::u32string original = buf;
            auto result = S2V(utf8::ConvertInPlace(buf));

            auto [begin, end] = utf8::CodePointIterator::FromSpan(result);
            auto decoded = CollectCodePoints(begin, end);
            Assert::AreEqual(original.size(), decoded.size());
            for (size_t i = 0; i < original.size(); ++i)
            {
                Assert::AreEqual(original[i], decoded[i]);
            }
        }

        // --- CodePointIterator ---

        TEST_METHOD(Iterator_PureAscii)
        {
            auto data = u8"Hello"sv;
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"Hello"sv);
        }

        TEST_METHOD(Iterator_TwoByteSequence)
        {
            auto data = u8"é"sv;
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"é"sv);
        }

        TEST_METHOD(Iterator_ThreeByteSequence)
        {
            auto data = u8"€"sv;
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"€"sv);
        }

        TEST_METHOD(Iterator_FourByteSequence)
        {
            auto data = u8"😀"sv;
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"😀"sv);
        }

        TEST_METHOD(Iterator_MixedSequences)
        {
            auto data = u8"Aé€😀"sv;
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"Aé€😀"sv);
        }

        TEST_METHOD(Iterator_ForwardBackwardConsistency)
        {
            auto data = u8"Aé€😀"sv;
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            VerifyForwardBackwardConsistency(begin, end);
        }

        TEST_METHOD(Iterator_InvalidLeadByte)
        {
            const char8_t data[] = { 0xFF, 0x41 };
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"\uFFFDA"sv);
        }

        TEST_METHOD(Iterator_TruncatedSequence)
        {
            const char8_t data[] = { 0xE2, 0x82 };
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            CollectCodePoints(begin, end);
            VerifyForwardBackwardConsistency(begin, end);
        }

        TEST_METHOD(Iterator_OverlongTwoByte)
        {
            const char8_t data[] = { 0xC0, 0x80 };
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"\uFFFD\uFFFD"sv);
        }

        TEST_METHOD(Iterator_SurrogateInUtf8)
        {
            // ED A0 80 = U+D800 (surrogate, invalid in UTF-8)
            const char8_t data[] = { 0xED, 0xA0, 0x80 };
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"\uFFFD"sv);
        }

        TEST_METHOD(Iterator_ForwardBackwardConsistency_Invalid)
        {
            const char8_t data[] = { 0xFF, 0xC3, 0xA9, 0xFE, 0xE2, 0x82, 0x41 };
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            VerifyForwardBackwardConsistency(begin, end);
        }

        TEST_METHOD(Iterator_TortureTest)
        {
            auto tortureTest = LoadTortureTestData();
            auto [begin, end] = utf8::CodePointIterator::FromSpan(tortureTest);

            size_t count = 0;
            for (auto it = begin; it != end; ++it)
            {
                ++count;
            }
            Assert::IsTrue(count > 0, L"Should have decoded at least one code point.");

            VerifyForwardBackwardConsistency(begin, end);

            for (size_t start = 0; start != tortureTest.size(); start += 1)
            {
                auto finish = start + 1;
                for (unsigned i = 1; i != 5 && finish <= tortureTest.size(); i += 1, finish += 1)
                {
                    auto [subBegin, subEnd] = utf8::CodePointIterator::FromSpan(tortureTest.subspan(start, finish - start));
                    VerifyForwardBackwardConsistency(subBegin, subEnd);
                }
            }
        }

        TEST_METHOD(Iterator_DecrementPastTruncatedLeadAtBegin)
        {
            const char8_t data[] = { 0xF0, 0x90 };
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            CollectCodePoints(begin, end);
            VerifyForwardBackwardConsistency(begin, end);
        }

        TEST_METHOD(Iterator_DecrementPastTruncated3ByteLeadAtBegin)
        {
            const char8_t data[] = { 0xE2, 0x82 };
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            CollectCodePoints(begin, end);
            VerifyForwardBackwardConsistency(begin, end);
        }

        TEST_METHOD(Iterator_DecrementPast4ByteLeadWith1TrailThenValid)
        {
            const char8_t data[] = { 0xF0, 0x90, 0x42 };
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            CollectCodePoints(begin, end);
            VerifyForwardBackwardConsistency(begin, end);
        }

        TEST_METHOD(Iterator_ValidMultiByteAtEndOfBuffer)
        {
            const char8_t data2[] = { 0xC3, 0xA9 };
            auto [begin2, end2] = utf8::CodePointIterator::FromSpan(data2);
            auto cps2 = CollectCodePoints(begin2, end2);
            Assert::AreEqual(size_t(1), cps2.size());
            Assert::AreEqual(char32_t(0xE9), cps2[0]);

            const char8_t data3[] = { 0xE2, 0x82, 0xAC };
            auto [begin3, end3] = utf8::CodePointIterator::FromSpan(data3);
            auto cps3 = CollectCodePoints(begin3, end3);
            Assert::AreEqual(size_t(1), cps3.size());
            Assert::AreEqual(char32_t(0x20AC), cps3[0]);

            const char8_t data4[] = { 0xF0, 0x9F, 0x98, 0x80 };
            auto [begin4, end4] = utf8::CodePointIterator::FromSpan(data4);
            auto cps4 = CollectCodePoints(begin4, end4);
            Assert::AreEqual(size_t(1), cps4.size());
            Assert::AreEqual(char32_t(0x1F600), cps4[0]);
        }

        TEST_METHOD(Iterator_Empty)
        {
            auto [begin, end] = utf8::CodePointIterator::FromSpan({});
            Assert::IsTrue(begin == end);
        }

        TEST_METHOD(Iterator_ByteOffset)
        {
            auto data = u8"Aé€"sv; // 1 + 2 + 3 = 6 bytes
            auto [begin, end] = utf8::CodePointIterator::FromSpan(data);
            Assert::AreEqual(size_t(0), begin.ByteOffset(data.data()));
            auto it = begin;
            ++it; // past 'A' (1 byte)
            Assert::AreEqual(size_t(1), it.ByteOffset(data.data()));
            ++it; // past 'é' (2 bytes)
            Assert::AreEqual(size_t(3), it.ByteOffset(data.data()));
            ++it; // past '€' (3 bytes)
            Assert::AreEqual(size_t(6), it.ByteOffset(data.data()));
            Assert::IsTrue(it == end);
        }

        TEST_METHOD(FromSpanAndByteOffset_ValidBoundaries)
        {
            auto data = u8"Aé€"sv; // byte offsets: 0='A', 1='é', 3='€', 6=end
            size_t validOffsets[] = { 0, 1, 3, 6 };
            for (auto offset : validOffsets)
            {
                auto [pos, begin, end] = utf8::CodePointIterator::FromSpanAndByteOffset(data, offset);
                Assert::IsTrue(pos != utf8::CodePointIterator(),
                    (std::wstring(L"Expected valid pos at offset ") + std::to_wstring(offset)).c_str());
                Assert::AreEqual(offset, pos.ByteOffset(data.data()));
            }
        }

        TEST_METHOD(FromSpanAndByteOffset_InvalidMidSequence)
        {
            auto data = u8"Aé€"sv; // byte offsets 2, 4, 5 are mid-sequence
            size_t invalidOffsets[] = { 2, 4, 5 };
            for (auto offset : invalidOffsets)
            {
                auto [pos, begin, end] = utf8::CodePointIterator::FromSpanAndByteOffset(data, offset);
                Assert::IsTrue(pos == utf8::CodePointIterator(),
                    (std::wstring(L"Expected invalid pos at offset ") + std::to_wstring(offset)).c_str());
            }
        }

        TEST_METHOD(FromSpanAndByteOffset_PastEnd)
        {
            auto data = u8"Aé€"sv;
            auto [pos, begin, end] = utf8::CodePointIterator::FromSpanAndByteOffset(data, 7);
            Assert::IsTrue(pos == utf8::CodePointIterator());
        }

        TEST_METHOD(FromSpanAndByteOffset_Empty)
        {
            auto [pos, begin, end] = utf8::CodePointIterator::FromSpanAndByteOffset({}, 0);
            Assert::IsTrue(pos == begin);
            Assert::IsTrue(begin == end);
        }

        TEST_METHOD(FromSpanAndByteOffset_FourByteSequence)
        {
            auto data = u8"😀X"sv; // 4 + 1 = 5 bytes; valid offsets: 0, 4, 5
            size_t validOffsets[] = { 0, 4, 5 };
            for (auto offset : validOffsets)
            {
                auto [pos, begin, end] = utf8::CodePointIterator::FromSpanAndByteOffset(data, offset);
                Assert::IsTrue(pos != utf8::CodePointIterator());
            }
            size_t invalidOffsets[] = { 1, 2, 3 };
            for (auto offset : invalidOffsets)
            {
                auto [pos, begin, end] = utf8::CodePointIterator::FromSpanAndByteOffset(data, offset);
                Assert::IsTrue(pos == utf8::CodePointIterator());
            }
        }

        TEST_METHOD(FromSpanAndByteOffset_TortureTest)
        {
            auto tortureTest = LoadTortureTestData();
            size_t testOffset = 0;
            auto [begin, end] = utf8::CodePointIterator::FromSpan(tortureTest);
            for (auto goodPos = begin;;)
            {
                auto goodOffset = goodPos.ByteOffset(tortureTest.data());

                // Test the invalid offsets.
                while (testOffset < goodOffset)
                {
                    auto [pos, begin2, end2] = utf8::CodePointIterator::FromSpanAndByteOffset(tortureTest, testOffset);
                    Assert::IsTrue(pos == utf8::CodePointIterator(),
                        (std::wstring(L"Expected invalid pos at offset ") + std::to_wstring(testOffset)).c_str());
                    testOffset += 1;
                }

                // Now test the valid offset.
                {
                    auto [pos, begin2, end2] = utf8::CodePointIterator::FromSpanAndByteOffset(tortureTest, testOffset);
                    Assert::IsTrue(pos != utf8::CodePointIterator(),
                        (std::wstring(L"Expected valid pos at offset ") + std::to_wstring(testOffset)).c_str());
                    Assert::AreEqual(goodOffset, pos.ByteOffset(tortureTest.data()));
                    testOffset += 1;
                }

                if (goodPos == end)
                {
                    break;
                }

                ++goodPos;
            }
        }
    };

    // ==================== utf16le tests ====================

    TEST_CLASS(Utf16LETests)
    {
    public:

        // --- Encode ---

        TEST_METHOD(Encode_BMP)
        {
            char16_t dst[2];
            Assert::AreEqual(1u, utf16le::Encode(dst, U'A'));
            Assert::IsTrue(dst[0] == u'A');
        }

        TEST_METHOD(Encode_SurrogatePair)
        {
            char16_t dst[2];
            unsigned n = utf16le::Encode(dst, 0x1F600);
            Assert::AreEqual(2u, n);
            Assert::IsTrue(dst[0] == 0xD83D);
            Assert::IsTrue(dst[1] == 0xDE00);
        }

        TEST_METHOD(Encode_InvalidCodePoint)
        {
            char16_t dst[2];
            unsigned n = utf16le::Encode(dst, 0x110000);
            Assert::AreEqual(1u, n);
            Assert::IsTrue(dst[0] == 0xFFFD);
        }

        // --- ConvertInPlace ---

        TEST_METHOD(ConvertInPlace_BMP)
        {
            std::u32string buf{ U"A€"sv };
            auto result = S2V(utf16le::ConvertInPlace(buf));
            Assert::IsTrue(result == u"A€"sv);
        }

        TEST_METHOD(ConvertInPlace_SurrogatePair)
        {
            std::u32string buf{ U"😀"sv };
            auto result = S2V(utf16le::ConvertInPlace(buf));
            Assert::IsTrue(result == u"😀"sv);
        }

        TEST_METHOD(ConvertInPlace_InvalidCodePoint)
        {
            std::u32string buf = { 0x110000 };
            auto result = S2V(utf16le::ConvertInPlace(buf));
            Assert::IsTrue(result == u"\uFFFD"sv);
        }

        TEST_METHOD(ConvertInPlace_RoundTrip)
        {
            std::u32string buf{ U"Aé€😀"sv };
            std::u32string original = buf;
            auto result = S2V(utf16le::ConvertInPlace(buf));

            auto [begin, end] = utf16le::CodePointIterator::FromSpan(result);
            auto decoded = CollectCodePoints(begin, end);
            Assert::AreEqual(original.size(), decoded.size());
            for (size_t i = 0; i < original.size(); ++i)
            {
                Assert::AreEqual(original[i], decoded[i]);
            }
        }

        // --- CodePointIterator ---

        TEST_METHOD(Iterator_BasicBMP)
        {
            auto data = u"Hello"sv;
            auto [begin, end] = utf16le::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"Hello"sv);
        }

        TEST_METHOD(Iterator_SurrogatePair)
        {
            auto data = u"😀"sv;
            auto [begin, end] = utf16le::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"😀"sv);
        }

        TEST_METHOD(Iterator_LoneHighSurrogate)
        {
            const char16_t data[] = { 0xD83D, 0x0041 };
            auto [begin, end] = utf16le::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"\uFFFDA"sv);
        }

        TEST_METHOD(Iterator_LoneLowSurrogate)
        {
            const char16_t data[] = { 0xDE00, 0x0041 };
            auto [begin, end] = utf16le::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"\uFFFDA"sv);
        }

        TEST_METHOD(Iterator_ForwardBackwardConsistency)
        {
            const char16_t data[] = { 0x0041, 0xD83D, 0xDE00, 0x20AC, 0xD800, 0x0042 };
            auto [begin, end] = utf16le::CodePointIterator::FromSpan(data);
            VerifyForwardBackwardConsistency(begin, end);
        }

        TEST_METHOD(Iterator_Empty)
        {
            auto [begin, end] = utf16le::CodePointIterator::FromSpan({});
            Assert::IsTrue(begin == end);
        }

        TEST_METHOD(Iterator_ByteOffset)
        {
            auto data = u"A😀B"sv; // A=1 unit, 😀=2 units, B=1 unit
            auto [begin, end] = utf16le::CodePointIterator::FromSpan(data);
            Assert::AreEqual(size_t(0), begin.ByteOffset(data.data()));
            auto it = begin;
            ++it; // past 'A' (2 bytes)
            Assert::AreEqual(size_t(2), it.ByteOffset(data.data()));
            ++it; // past 😀 (4 bytes)
            Assert::AreEqual(size_t(6), it.ByteOffset(data.data()));
            ++it; // past 'B' (2 bytes)
            Assert::AreEqual(size_t(8), it.ByteOffset(data.data()));
            Assert::IsTrue(it == end);
        }

        TEST_METHOD(FromSpanAndByteOffset_Boundaries)
        {
            auto data = u"A😀B"sv; // byte offsets: 0='A', 2=high surrogate, 6='B', 8=end
            size_t validOffsets[] = { 0, 2, 6, 8 };
            for (size_t offset = 0; offset != data.size(); offset += 1)
            {
                auto [pos, begin, end] = utf16le::CodePointIterator::FromSpanAndByteOffset(data, offset);
                Assert::AreEqual(size_t(0), begin.ByteOffset(data.data()));
                Assert::AreEqual(data.size() * sizeof(char16_t), end.ByteOffset(data.data()));
                if (std::find(std::begin(validOffsets), std::end(validOffsets), offset) != std::end(validOffsets))
                {
                    Assert::IsTrue(pos != utf16le::CodePointIterator());
                    Assert::AreEqual(offset, pos.ByteOffset(data.data()));
                }
                else
                {
                    Assert::IsTrue(pos == utf16le::CodePointIterator());
                }
            }
        }

        TEST_METHOD(FromSpanAndByteOffset_InvalidLowSurrogate)
        {
            auto data = u"A😀B"sv; // offset 4 = low surrogate
            auto [pos, begin, end] = utf16le::CodePointIterator::FromSpanAndByteOffset(data, 4);
            Assert::IsTrue(pos == utf16le::CodePointIterator());
        }

        TEST_METHOD(FromSpanAndByteOffset_LowSurrogateAtBeginPlus1)
        {
            // Surrogate pair only: offset 2 = low surrogate at element index 1.
            auto data = u"😀"sv;
            auto [pos, begin, end] = utf16le::CodePointIterator::FromSpanAndByteOffset(data, 2);
            Assert::IsTrue(pos == utf16le::CodePointIterator());
        }

        TEST_METHOD(FromSpanAndByteOffset_OddByteOffset)
        {
            auto data = u"AB"sv;
            auto [pos, begin, end] = utf16le::CodePointIterator::FromSpanAndByteOffset(data, 1);
            Assert::IsTrue(pos == utf16le::CodePointIterator());
        }

        TEST_METHOD(FromSpanAndByteOffset_PastEnd)
        {
            auto data = u"AB"sv;
            auto [pos, begin, end] = utf16le::CodePointIterator::FromSpanAndByteOffset(data, 6);
            Assert::IsTrue(pos == utf16le::CodePointIterator());
        }

        TEST_METHOD(FromSpanAndByteOffset_Empty)
        {
            auto [pos, begin, end] = utf16le::CodePointIterator::FromSpanAndByteOffset({}, 0);
            Assert::IsTrue(pos == begin);
            Assert::IsTrue(begin == end);
        }
    };

    // ==================== utf16be tests ====================

    TEST_CLASS(Utf16BETests)
    {
    public:

        // --- Encode ---

        TEST_METHOD(Encode_BMP)
        {
            char16_t dst[2];
            Assert::AreEqual(1u, utf16be::Encode(dst, U'A'));
            // 'A' = 0x0041, byte-swapped = 0x4100
            Assert::IsTrue(dst[0] == 0x4100);
        }

        TEST_METHOD(Encode_SurrogatePair)
        {
            char16_t dst[2];
            unsigned n = utf16be::Encode(dst, 0x1F600);
            Assert::AreEqual(2u, n);
            // D83D byte-swapped = 3DD8, DE00 byte-swapped = 00DE
            Assert::IsTrue(dst[0] == 0x3DD8);
            Assert::IsTrue(dst[1] == 0x00DE);
        }

        // --- ConvertInPlace ---

        TEST_METHOD(ConvertInPlace_BMP)
        {
            std::u32string buf{ U"A€"sv };
            auto result = ByteSwap16(utf16be::ConvertInPlace(buf));
            Assert::IsTrue(result == u"A€"sv);
        }

        TEST_METHOD(ConvertInPlace_SurrogatePair)
        {
            std::u32string buf{ U"😀"sv };
            auto result = ByteSwap16(utf16be::ConvertInPlace(buf));
            Assert::IsTrue(result == u"😀"sv);
        }

        TEST_METHOD(ConvertInPlace_RoundTrip)
        {
            std::u32string buf{ U"Aé€😀"sv };
            std::u32string original = buf;
            auto result = utf16be::ConvertInPlace(buf);

            auto [begin, end] = utf16be::CodePointIterator::FromSpan(result);
            auto decoded = CollectCodePoints(begin, end);
            Assert::AreEqual(original.size(), decoded.size());
            for (size_t i = 0; i < original.size(); ++i)
            {
                Assert::AreEqual(original[i], decoded[i]);
            }
        }

        // --- CodePointIterator ---

        TEST_METHOD(Iterator_BasicBMP)
        {
            char16_t dataBuf[] = u"Hello";
            ByteSwap16(dataBuf);
            std::u16string_view data(dataBuf);
            auto [begin, end] = utf16be::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"Hello"sv);
        }

        TEST_METHOD(Iterator_SurrogatePair)
        {
            char16_t dataBuf[] = u"😀";
            ByteSwap16(dataBuf);
            std::u16string_view data(dataBuf);
            auto [begin, end] = utf16be::CodePointIterator::FromSpan(data);
            auto cps = CollectCodePoints(begin, end);
            Assert::IsTrue(VectorToView(cps) == U"😀"sv);
        }

        TEST_METHOD(Iterator_ForwardBackwardConsistency)
        {
            char16_t data[] = { 0x4100, 0x3DD8, 0x00DE, 0xAC20, 0x00D8, 0x4200 };
            auto [begin, end] = utf16be::CodePointIterator::FromSpan(data);
            VerifyForwardBackwardConsistency(begin, end);
        }
    };
}
