namespace UnicodeRegEx.Tests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;

    [TestClass]
    public class TranscodeAndEscapeTests
    {
        [TestMethod]
        public void Transcode_Utf8ToString()
        {
            var bytes = TestHelpers.Encode("caf\u00e9", RegExEncoding.Utf8);

            var result = RegEx.Transcode(new RegExInput(bytes, RegExEncoding.Utf8));

            Assert.AreEqual("caf\u00e9", result);
        }

        [TestMethod]
        public void Transcode_Latin1ToString()
        {
            var bytes = TestHelpers.Encode("\u00e1\u00e9\u00ed", RegExEncoding.Latin1);

            var result = RegEx.Transcode(new RegExInput(bytes, RegExEncoding.Latin1));

            Assert.AreEqual("\u00e1\u00e9\u00ed", result);
        }

        [TestMethod]
        public void Transcode_Utf16BeToString()
        {
            var bytes = TestHelpers.Encode("hello", RegExEncoding.Utf16BE);

            var result = RegEx.Transcode(new RegExInput(bytes, RegExEncoding.Utf16BE));

            Assert.AreEqual("hello", result);
        }

        [TestMethod]
        public void Transcode_Utf16LePassThrough()
        {
            var result = RegEx.Transcode("hello");

            Assert.AreEqual("hello", result);
        }

        [TestMethod]
        public void TranscodeTo_WritesConvertedBytes()
        {
            using var stream = RegEx.CreateMemoryStream();

            RegEx.TranscodeTo("caf\u00e9", stream.Value!, RegExEncoding.Utf8);

            var bytes = TestHelpers.ReadAllBytes(stream.Value!);
            CollectionAssert.AreEqual(TestHelpers.Encode("caf\u00e9", RegExEncoding.Utf8), bytes);
        }

        [TestMethod]
        public void EscapePatternLiteral_EscapesMetacharacters_Perl()
        {
            var escaped = RegEx.EscapePatternLiteral("a?b*c");

            Assert.AreEqual(@"a\?b\*c", escaped);
        }

        [TestMethod]
        public void EscapePatternLiteral_RoundTripsAsLiteralMatch()
        {
            const string literal = "1+1=2 (really?)";
            var escaped = RegEx.EscapePatternLiteral(literal);

            var regex = RegEx.Create(escaped);
            var text = regex.Search("answer: 1+1=2 (really?) yes", default, "<none>",
                m => TestHelpers.WholeMatchText(m));

            Assert.AreEqual(literal, text);
        }

        [TestMethod]
        public void EscapePatternLiteral_BasicSyntax_DoesNotEscapeQuestionMark()
        {
            // In POSIX basic syntax "?" is not a metacharacter, but "*" is.
            var escaped = RegEx.EscapePatternLiteral("a?b*c", RegExSyntaxFlags.Basic);

            Assert.AreEqual(@"a?b\*c", escaped);
        }

        [TestMethod]
        public void EscapeFormatLiteral_RoundTripsAsLiteralReplacement()
        {
            const string literal = "price: $5";
            var escapedFormat = RegEx.EscapeFormatLiteral(literal);

            var regex = RegEx.Create("X");
            var result = regex.Replace("X", escapedFormat);

            Assert.AreEqual(literal, result);
        }

        [TestMethod]
        public void GetEscapePatternLiteralChars_Perl_ContainsCommonMetacharacters()
        {
            var chars = RegEx.GetEscapePatternLiteralChars();

            foreach (var c in new[] { '.', '[', '*', '+', '?', '(', ')', '|', '^', '$' })
            {
                StringAssert.Contains(chars, c.ToString());
            }
        }

        [TestMethod]
        public void GetEscapeFormatLiteralChars_PerlContainsDollar_SedContainsAmpersand()
        {
            var perl = RegEx.GetEscapeFormatLiteralChars(RegExFormatFlags.Perl);
            StringAssert.Contains(perl, "$");
            StringAssert.Contains(perl, "\\");

            var sed = RegEx.GetEscapeFormatLiteralChars(RegExFormatFlags.Sed);
            StringAssert.Contains(sed, "&");
            StringAssert.Contains(sed, "\\");
        }
    }
}
