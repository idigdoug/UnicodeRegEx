namespace UnicodeRegEx.Tests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;

    [TestClass]
    public class LineCounterTests
    {
        // Advances a counter over the bytes in one step and returns the resulting line number.
        private static unsafe long LineAt(byte[] data, int codePage, nuint target)
        {
            fixed (byte* p = data)
            {
                var counter = new RegExLineCounter(new RegExPinnedBytes(p, (nuint)data.Length), codePage);
                counter.AdvanceTo(target);
                return (long)counter.LineNumber;
            }
        }

        // Advances a counter in one step and returns the resulting (line, column) pair.
        private static unsafe (int Line, int Column) PositionAt(byte[] data, int codePage, nuint target)
        {
            fixed (byte* p = data)
            {
                var counter = new RegExLineCounter(new RegExPinnedBytes(p, (nuint)data.Length), codePage);
                counter.AdvanceTo(target);
                return ((int)counter.LineNumber, (int)counter.Column);
            }
        }

        // Advances a counter in two steps (split at 'mid') to exercise cross-call CR carry.
        private static unsafe long LineInTwoSteps(byte[] data, int codePage, nuint mid, nuint target)
        {
            fixed (byte* p = data)
            {
                var counter = new RegExLineCounter(new RegExPinnedBytes(p, (nuint)data.Length), codePage);
                counter.AdvanceTo(mid);
                counter.AdvanceTo(target);
                return (long)counter.LineNumber;
            }
        }

        [TestMethod]
        public unsafe void Fresh_IsLineZero()
        {
            var data = TestHelpers.Encode("a\nb", RegExCodePage.Utf8);
            fixed (byte* p = data)
            {
                var counter = new RegExLineCounter(new RegExPinnedBytes(p, (nuint)data.Length), RegExCodePage.Utf8);
                Assert.AreEqual(0, (long)counter.LineNumber);
                Assert.AreEqual((nuint)0, counter.Offset);
                Assert.AreEqual(1, (long)counter.Column); // a fresh counter reads as line 0, column 1
            }
        }

        [TestMethod]
        public void FirstByte_MovesToLineOne()
        {
            var data = TestHelpers.Encode("abc", RegExCodePage.Utf8); // no newlines
            Assert.AreEqual(1, LineAt(data, RegExCodePage.Utf8, 0)); // any AdvanceTo puts the cursor on line 1
            Assert.AreEqual(1, LineAt(data, RegExCodePage.Utf8, 1));
            Assert.AreEqual(1, LineAt(data, RegExCodePage.Utf8, 3));
        }

        [TestMethod]
        public void Lf_AdvancesLine()
        {
            var data = TestHelpers.Encode("a\nb", RegExCodePage.Utf8); // a=0, \n=1, b=2
            Assert.AreEqual(1, LineAt(data, RegExCodePage.Utf8, 1)); // at the LF, still line 1
            Assert.AreEqual(2, LineAt(data, RegExCodePage.Utf8, 2)); // past the LF, line 2
            Assert.AreEqual(2, LineAt(data, RegExCodePage.Utf8, 3)); // at 'b' end, line 2
        }

        [TestMethod]
        public void TrailingLf_LeavesCursorOnNextLine()
        {
            var data = TestHelpers.Encode("a\n", RegExCodePage.Utf8);
            Assert.AreEqual(2, LineAt(data, RegExCodePage.Utf8, (nuint)data.Length));
        }

        [TestMethod]
        public void TrailingCr_IsCountedImmediately()
        {
            // A CR at the end of the consumed input counts on sight (not deferred).
            var data = TestHelpers.Encode("a\r", RegExCodePage.Utf8);
            Assert.AreEqual(2, LineAt(data, RegExCodePage.Utf8, (nuint)data.Length));
        }

        [TestMethod]
        public void Crlf_CountsAsOneBreak()
        {
            var data = TestHelpers.Encode("a\r\nb", RegExCodePage.Utf8);
            Assert.AreEqual(2, LineAt(data, RegExCodePage.Utf8, (nuint)data.Length));
        }

        [TestMethod]
        public void LoneCr_CountsAsBreak()
        {
            var data = TestHelpers.Encode("a\rb", RegExCodePage.Utf8); // classic-Mac line ending
            Assert.AreEqual(2, LineAt(data, RegExCodePage.Utf8, (nuint)data.Length));
        }

        [TestMethod]
        public void MultipleBreaks_MixedConventions()
        {
            // "a\nb\r\nc\rd" -> 4 lines: a | b | c | d
            var data = TestHelpers.Encode("a\nb\r\nc\rd", RegExCodePage.Utf8);
            Assert.AreEqual(4, LineAt(data, RegExCodePage.Utf8, (nuint)data.Length));
        }

        [TestMethod]
        public void Crlf_SplitAcrossAdvances_NotDoubleCounted()
        {
            // "a\r\nb": split between the CR and the LF (mid = 2, just after CR).
            var data = TestHelpers.Encode("a\r\nb", RegExCodePage.Utf8);
            // The CR is counted on sight, so consuming "a\r" already advances to line 2.
            Assert.AreEqual(2, LineInTwoSteps(data, RegExCodePage.Utf8, 2, 2));
            // The LF that follows is the CR-LF partner and is suppressed -> still line 2 (not 3).
            Assert.AreEqual(2, LineInTwoSteps(data, RegExCodePage.Utf8, 2, (nuint)data.Length));
        }

        [TestMethod]
        public void LoneCr_SplitAcrossAdvances_CountedImmediately()
        {
            // "a\rb": the CR is counted when "a\r" is consumed; the following non-LF keeps line 2.
            var data = TestHelpers.Encode("a\rb", RegExCodePage.Utf8);
            Assert.AreEqual(2, LineInTwoSteps(data, RegExCodePage.Utf8, 2, 2)); // CR counted on sight
            Assert.AreEqual(2, LineInTwoSteps(data, RegExCodePage.Utf8, 2, (nuint)data.Length));
        }

        [TestMethod]
        public void Utf16Le_Lf_AdvancesLine()
        {
            var data = TestHelpers.Encode("a\nb", RegExCodePage.Utf16LE);
            Assert.AreEqual(2, LineAt(data, RegExCodePage.Utf16LE, (nuint)data.Length));
        }

        [TestMethod]
        public void Utf16Be_Crlf_CountsAsOneBreak()
        {
            var data = TestHelpers.Encode("a\r\nb", RegExCodePage.Utf16BE);
            Assert.AreEqual(2, LineAt(data, RegExCodePage.Utf16BE, (nuint)data.Length));
        }

        [TestMethod]
        public void Utf16Le_Crlf_SplitAcrossAdvances_NotDoubleCounted()
        {
            // CR is at byte offset 2 (units: 'a'=0..2, CR=2..4, LF=4..6, 'b'=6..8). Split after CR unit.
            var data = TestHelpers.Encode("a\r\nb", RegExCodePage.Utf16LE);
            Assert.AreEqual(2, LineInTwoSteps(data, RegExCodePage.Utf16LE, 4, (nuint)data.Length));
        }

        [TestMethod]
        public void Column_CountsCodeUnitsFromLineStart()
        {
            // AdvanceTo(n) reports the position AFTER consuming n bytes (where the n-th char ends / the next
            // begins). "abc\ndef": offsets a=0 b=1 c=2 \n=3 d=4 e=5 f=6.
            var data = TestHelpers.Encode("abc\ndef", RegExCodePage.Utf8);
            Assert.AreEqual((1, 2), PositionAt(data, RegExCodePage.Utf8, 1)); // consumed 'a' -> col 2
            Assert.AreEqual((1, 4), PositionAt(data, RegExCodePage.Utf8, 3)); // consumed 'abc' -> col 4 (at LF)
            Assert.AreEqual((2, 1), PositionAt(data, RegExCodePage.Utf8, 4)); // consumed through LF -> line 2, col 1
            Assert.AreEqual((2, 2), PositionAt(data, RegExCodePage.Utf8, 5)); // consumed 'd' -> col 2
            Assert.AreEqual((2, 4), PositionAt(data, RegExCodePage.Utf8, 7)); // consumed 'def' -> col 4, end
        }

        [TestMethod]
        public void Column_AfterCrlf_ResetsToOne()
        {
            // "a\r\nbc": the new line starts just past the LF, so the position right after the LF is col 1.
            var data = TestHelpers.Encode("a\r\nbc", RegExCodePage.Utf8); // a=0 \r=1 \n=2 b=3 c=4
            Assert.AreEqual((2, 1), PositionAt(data, RegExCodePage.Utf8, 3)); // consumed through LF -> col 1
            Assert.AreEqual((2, 2), PositionAt(data, RegExCodePage.Utf8, 4)); // consumed 'b' -> col 2
        }

        [TestMethod]
        public void Column_Utf16_MeasuredInCodeUnits()
        {
            // "ab\ncd" in UTF-16LE: 2 bytes per unit. Line 2 starts after the LF unit (byte 6).
            var data = TestHelpers.Encode("ab\ncd", RegExCodePage.Utf16LE); // a=0..2 b=2..4 \n=4..6 c=6..8 d=8..10
            Assert.AreEqual((1, 3), PositionAt(data, RegExCodePage.Utf16LE, 4)); // consumed 'ab' -> col 3
            Assert.AreEqual((2, 1), PositionAt(data, RegExCodePage.Utf16LE, 6)); // consumed through LF -> col 1
            Assert.AreEqual((2, 2), PositionAt(data, RegExCodePage.Utf16LE, 8)); // consumed 'c' -> col 2
        }
    }
}
