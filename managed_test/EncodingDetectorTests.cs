namespace UnicodeRegEx.Tests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;

    [TestClass]
    public class EncodingDetectorTests
    {
        private const int Latin1 = RegExCodePage.Latin1;

        private static unsafe EncodingDetectionResult Detect(byte[] data, int defaultCodePage)
        {
            return Detect(data, defaultCodePage, EncodingDetectionOptions.Default);
        }

        private static unsafe EncodingDetectionResult Detect(byte[] data, int defaultCodePage, EncodingDetectionOptions options)
        {
            fixed (byte* p = data)
            {
                return EncodingDetector.Detect(new RegExPinnedBytes(p, (nuint)data.Length), data.Length, defaultCodePage, options);
            }
        }

        // Helper: all steps except the ones named in 'without'.
        private static EncodingDetectionOptions Except(EncodingDetectionSteps without) =>
            new EncodingDetectionOptions(EncodingDetectionSteps.All & ~without);

        [TestMethod]
        public void Utf8Bom_IsUtf8_NotBinary()
        {
            var data = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };
            var r = Detect(data, Latin1);
            Assert.AreEqual(RegExCodePage.Utf8, r.CodePage);
            Assert.IsFalse(r.LooksBinary);
        }

        [TestMethod]
        public void Utf16LeBom_IsUtf16Le_NotBinary_EvenWithNuls()
        {
            // FF FE BOM, then "hi" in UTF-16LE (embedded NULs at odd positions).
            var data = new byte[] { 0xFF, 0xFE, (byte)'h', 0x00, (byte)'i', 0x00 };
            var r = Detect(data, Latin1);
            Assert.AreEqual(RegExCodePage.Utf16LE, r.CodePage);
            Assert.IsFalse(r.LooksBinary); // UTF-16 is exempt from the NUL binary check
        }

        [TestMethod]
        public void Utf16BeBom_IsUtf16Be_NotBinary()
        {
            var data = new byte[] { 0xFE, 0xFF, 0x00, (byte)'h', 0x00, (byte)'i' };
            var r = Detect(data, Latin1);
            Assert.AreEqual(RegExCodePage.Utf16BE, r.CodePage);
            Assert.IsFalse(r.LooksBinary);
        }

        [TestMethod]
        public void NoBom_PlainText_UsesDefault_NotBinary()
        {
            var data = new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };
            var r = Detect(data, Latin1);
            Assert.AreEqual(Latin1, r.CodePage);
            Assert.IsFalse(r.LooksBinary);
        }

        [TestMethod]
        public void NoBom_WithNul_IsBinary()
        {
            var data = new byte[] { (byte)'a', 0x00, (byte)'b' };
            var r = Detect(data, Latin1);
            Assert.AreEqual(Latin1, r.CodePage);
            Assert.IsTrue(r.LooksBinary);
        }

        [TestMethod]
        public void NulBeyondScanWindow_IsNotBinary()
        {
            // 8000 non-NUL bytes, then a NUL at index 8000 (outside the scan window).
            var data = new byte[8001];
            for (var i = 0; i < 8000; i++)
            {
                data[i] = (byte)'a';
            }

            data[8000] = 0x00;

            var r = Detect(data, Latin1);
            Assert.IsFalse(r.LooksBinary);
        }

        [TestMethod]
        public void TwoByteUtf16LeBom_IsRecognized()
        {
            var data = new byte[] { 0xFF, 0xFE };
            var r = Detect(data, Latin1);
            Assert.AreEqual(RegExCodePage.Utf16LE, r.CodePage);
        }

        [TestMethod]
        public void SingleByte_DoesNotFalseMatchBom()
        {
            var data = new byte[] { 0xEF };
            var r = Detect(data, Latin1);
            Assert.AreEqual(Latin1, r.CodePage);
            Assert.IsFalse(r.LooksBinary);
        }

        // ---- UTF-16 NUL-parity heuristic (no BOM)

        // Builds ASCII-heavy UTF-16 bytes for the given text, in LE (NUL after each char) or BE (before).
        private static byte[] Utf16NoBom(string text, bool bigEndian)
        {
            var data = new byte[text.Length * 2];
            for (var i = 0; i < text.Length; i++)
            {
                if (bigEndian)
                {
                    data[i * 2] = 0x00;
                    data[i * 2 + 1] = (byte)text[i];
                }
                else
                {
                    data[i * 2] = (byte)text[i];
                    data[i * 2 + 1] = 0x00;
                }
            }

            return data;
        }

        [TestMethod]
        public void NoBom_AsciiHeavyUtf16Le_DetectedByParity()
        {
            var data = Utf16NoBom("hello world!!", bigEndian: false); // 13 chars -> 13 odd NULs
            var r = Detect(data, Latin1);
            Assert.AreEqual(RegExCodePage.Utf16LE, r.CodePage);
            Assert.IsFalse(r.LooksBinary);
        }

        [TestMethod]
        public void NoBom_AsciiHeavyUtf16Be_DetectedByParity()
        {
            var data = Utf16NoBom("hello world!!", bigEndian: true); // 13 even NULs
            var r = Detect(data, Latin1);
            Assert.AreEqual(RegExCodePage.Utf16BE, r.CodePage);
            Assert.IsFalse(r.LooksBinary);
        }

        [TestMethod]
        public void NoBom_FewNuls_BelowMinCount_NotUtf16()
        {
            // "hi" in UTF-16LE: only 2 NULs, under the minimum -> not asserted as UTF-16.
            var data = Utf16NoBom("hi", bigEndian: false);
            var r = Detect(data, Latin1);
            Assert.AreEqual(Latin1, r.CodePage);
            Assert.IsTrue(r.LooksBinary); // falls through to default; the NUL makes it look binary
        }

        [TestMethod]
        public void NoBom_NulsAtBothParities_NotUtf16_IsBinary()
        {
            // NULs at both parities with no dominance (binary-like) -> not UTF-16, and binary.
            var data = new byte[20]; // all-NUL: nulEven == nulOdd, neither parity dominates
            var r = Detect(data, Latin1);
            Assert.AreEqual(Latin1, r.CodePage);
            Assert.IsTrue(r.LooksBinary);
        }

        // ---- UTF-8 strict heuristic (no BOM)

        [TestMethod]
        public void NoBom_ValidUtf8Multibyte_DetectedAsUtf8()
        {
            // "café" = 63 61 66 C3 A9 (one 2-byte sequence) -> positive UTF-8 evidence.
            var data = new byte[] { 0x63, 0x61, 0x66, 0xC3, 0xA9 };
            var r = Detect(data, Latin1);
            Assert.AreEqual(RegExCodePage.Utf8, r.CodePage);
            Assert.IsFalse(r.LooksBinary);
        }

        [TestMethod]
        public void NoBom_PlainAscii_IsNotUtf8_UsesDefault()
        {
            // No multibyte sequence -> no positive UTF-8 evidence -> default code page.
            var data = new byte[] { 0x68, 0x65, 0x6C, 0x6C, 0x6F };
            var r = Detect(data, Latin1);
            Assert.AreEqual(Latin1, r.CodePage);
        }

        [TestMethod]
        public void NoBom_InvalidUtf8_IsNotUtf8()
        {
            // C3 28: lead byte followed by a non-continuation byte -> invalid -> not UTF-8.
            var data = new byte[] { 0x61, 0xC3, 0x28, 0x62 };
            var r = Detect(data, Latin1);
            Assert.AreEqual(Latin1, r.CodePage);
        }

        [TestMethod]
        public void NoBom_OverlongUtf8_IsRejected()
        {
            // C0 80 is an overlong encoding of NUL -> invalid lead byte (0xC0) -> not UTF-8.
            var data = new byte[] { 0x61, 0xC0, 0x80 };
            var r = Detect(data, Latin1);
            Assert.AreNotEqual(RegExCodePage.Utf8, r.CodePage);
        }

        [TestMethod]
        public void NoBom_FileEndsMidSequence_IsInvalid_NotUtf8()
        {
            // The whole file ends with a lone lead byte (C3) — a truncated/malformed trailing sequence.
            // Because this is the true end of file (not the scan window), it is invalid, so NOT UTF-8.
            var data = new byte[] { 0x63, 0xC3, 0xA9, 0xC3 };
            var r = Detect(data, Latin1);
            Assert.AreNotEqual(RegExCodePage.Utf8, r.CodePage);
        }

        [TestMethod]
        public void WindowTruncatedTrailingSequence_IsIncompleteNotInvalid()
        {
            // A file larger than the 8000-byte scan window, whose only multibyte sequence sits earlier,
            // and which has a lead byte straddling the window boundary. The window (not the file) cut the
            // trailing sequence, so it is incomplete (ignored) and the earlier sequence still qualifies.
            var data = new byte[8100];
            data[0] = 0xC3; // complete 2-byte sequence at the very start (é)
            data[1] = 0xA9;
            for (var i = 2; i < data.Length; i++)
            {
                data[i] = (byte)'a';
            }

            data[7999] = 0xC3; // lead byte at the last in-window position; continuation is past the window

            var r = Detect(data, Latin1);
            Assert.AreEqual(RegExCodePage.Utf8, r.CodePage);
        }

        // ---- Binary control-byte-ratio heuristic

        [TestMethod]
        public void NoNul_HighControlRatio_IsBinary()
        {
            // No NUL, but mostly non-text control bytes (0x01..0x08) -> binary via the ratio check.
            var data = new byte[100];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(1 + (i % 8)); // 0x01..0x08, all non-text controls
            }

            var r = Detect(data, Latin1);
            Assert.IsTrue(r.LooksBinary);
        }

        [TestMethod]
        public void TextWhitespaceControls_AreNotBinary()
        {
            // Tabs, LF, CR, FF are text whitespace and must not count toward the binary ratio.
            var data = new byte[100];
            for (var i = 0; i < data.Length; i++)
            {
                var w = new byte[] { 0x09, 0x0A, 0x0C, 0x0D };
                data[i] = w[i % w.Length];
            }

            var r = Detect(data, Latin1);
            Assert.IsFalse(r.LooksBinary);
        }

        // ---- Per-step toggles (slice 3): disabling a step changes the verdict accordingly.

        [TestMethod]
        public void DisableBom_Utf16LeBom_IsNotRecognized()
        {
            // FF FE then "h" (one NUL): with BOM off and too few NULs for the UTF-16 heuristic -> default.
            var data = new byte[] { 0xFF, 0xFE, (byte)'h', 0x00 };
            var r = Detect(data, Latin1, Except(EncodingDetectionSteps.Bom));
            Assert.AreEqual(Latin1, r.CodePage);
        }

        [TestMethod]
        public void DisableUtf16Heuristic_NoBomUtf16_FallsToDefault()
        {
            var data = Utf16NoBom("hello world!!", bigEndian: false); // would be UTF-16LE by parity
            var r = Detect(data, Latin1, Except(EncodingDetectionSteps.Utf16Heuristic));
            Assert.AreEqual(Latin1, r.CodePage);
            Assert.IsTrue(r.LooksBinary); // now non-UTF-16 with NULs -> binary
        }

        [TestMethod]
        public void DisableUtf8Heuristic_NoBomUtf8_FallsToDefault()
        {
            var data = new byte[] { 0x63, 0x61, 0x66, 0xC3, 0xA9 }; // "café", UTF-8 by heuristic
            var r = Detect(data, Latin1, Except(EncodingDetectionSteps.Utf8Heuristic));
            Assert.AreEqual(Latin1, r.CodePage);
        }

        [TestMethod]
        public void DisableBinaryNul_NulFile_IsNotBinary()
        {
            // One NUL in 5 bytes: 20% control bytes, under the control-ratio threshold, so with the NUL
            // check disabled the only remaining binary trigger does not fire.
            var data = new byte[] { (byte)'a', 0x00, (byte)'b', (byte)'c', (byte)'d' };
            var r = Detect(data, Latin1, Except(EncodingDetectionSteps.BinaryNul));
            Assert.IsFalse(r.LooksBinary);
        }

        [TestMethod]
        public void DisableBinaryControlRatio_HighControlFile_IsNotBinary()
        {
            var data = new byte[100];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(1 + (i % 8)); // 0x01..0x08, no NUL
            }

            var r = Detect(data, Latin1, Except(EncodingDetectionSteps.BinaryControlRatio));
            Assert.IsFalse(r.LooksBinary); // control-ratio off, and no NUL -> not binary
        }

        [TestMethod]
        public void NoSteps_AlwaysDefault_NotBinary()
        {
            var options = new EncodingDetectionOptions(EncodingDetectionSteps.None);

            // A UTF-8 BOM file: with no steps, even the BOM is ignored -> default, not binary.
            var bom = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i' };
            var r = Detect(bom, Latin1, options);
            Assert.AreEqual(Latin1, r.CodePage);
            Assert.IsFalse(r.LooksBinary);

            // A NUL-containing file: with binary checks off -> not binary.
            var nul = new byte[] { (byte)'a', 0x00, (byte)'b' };
            r = Detect(nul, Latin1, options);
            Assert.AreEqual(Latin1, r.CodePage);
            Assert.IsFalse(r.LooksBinary);
        }
    }
}
