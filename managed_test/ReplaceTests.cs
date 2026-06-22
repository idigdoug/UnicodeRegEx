namespace UnicodeRegEx.Tests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;

    [TestClass]
    public class ReplaceTests
    {
        [TestMethod]
        public void Replace_AllOccurrences()
        {
            var regex = RegEx.Create("a");

            var result = regex.Replace("banana", "X");

            Assert.AreEqual("bXnXnX", result);
        }

        [TestMethod]
        public void Replace_WithCaptureReferences()
        {
            var regex = RegEx.Create(@"(\w+)@(\w+)");

            var result = regex.Replace("user@host", "$2.$1");

            Assert.AreEqual("host.user", result);
        }

        [TestMethod]
        public void Replace_FirstOnly()
        {
            var regex = RegEx.Create("a");
            var options = new RegExReplaceOptions { FormatFlags = RegExFormatFlags.FirstOnly };

            var result = regex.Replace("banana", "X", options);

            Assert.AreEqual("bXnana", result);
        }

        [TestMethod]
        public void Replace_NoCopy_OmitsUnmatchedText()
        {
            var regex = RegEx.Create("a");
            var options = new RegExReplaceOptions { FormatFlags = RegExFormatFlags.NoCopy };

            var result = regex.Replace("banana", "X", options);

            Assert.AreEqual("XXX", result);
        }

        [TestMethod]
        public void Replace_StartByteOffset_CopiesPrefixUnchanged()
        {
            var regex = RegEx.Create("a");

            // Skip the first 4 bytes (2 UTF-16 chars "ba"); they are copied verbatim,
            // so only the "a" characters at/after the offset are replaced.
            var options = new RegExReplaceOptions { StartByteOffset = 4 };
            var result = regex.Replace("banana", "X", options);

            Assert.AreEqual("banXnX", result);
        }

        [TestMethod]
        public void Replace_Latin1_LeavesUntouchedBytesIntact()
        {
            var regex = RegEx.Create("[0-9]+");

            var bytes = TestHelpers.Encode("id=42;", RegExCodePage.Latin1);
            var result = regex.Replace(new RegExInput(bytes, RegExCodePage.Latin1), "N");

            Assert.AreEqual("id=N;", result);
        }

        [TestMethod]
        public void ReplaceTo_WritesToMemoryStream()
        {
            var regex = RegEx.Create("a");

            using var stream = RegEx.CreateMemoryStream();
            regex.ReplaceTo("banana", stream, RegExCodePage.Utf16LE, "X");

            var text = TestHelpers.ReadAllText(stream, RegExCodePage.Utf16LE);
            Assert.AreEqual("bXnXnX", text);
        }

        [TestMethod]
        public void ReplaceTo_TranscodesOutputCodePage()
        {
            var regex = RegEx.Create("a");

            using var stream = RegEx.CreateMemoryStream();
            regex.ReplaceTo("banana", stream, RegExCodePage.Utf8, "X");

            var bytes = TestHelpers.ReadAllBytes(stream);
            Assert.AreEqual("bXnXnX", TestHelpers.Decode(bytes, RegExCodePage.Utf8));
        }
    }
}
