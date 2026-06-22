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
            var bytes = TestHelpers.Encode("caf\u00e9", RegExCodePage.Utf8);

            var result = RegEx.Transcode(new RegExInput(bytes, RegExCodePage.Utf8));

            Assert.AreEqual("caf\u00e9", result);
        }

        [TestMethod]
        public void Transcode_Latin1ToString()
        {
            var bytes = TestHelpers.Encode("\u00e1\u00e9\u00ed", RegExCodePage.Latin1);

            var result = RegEx.Transcode(new RegExInput(bytes, RegExCodePage.Latin1));

            Assert.AreEqual("\u00e1\u00e9\u00ed", result);
        }

        [TestMethod]
        public void Transcode_Utf16BeToString()
        {
            var bytes = TestHelpers.Encode("hello", RegExCodePage.Utf16BE);

            var result = RegEx.Transcode(new RegExInput(bytes, RegExCodePage.Utf16BE));

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

            RegEx.TranscodeTo("caf\u00e9", stream, RegExCodePage.Utf8);

            var bytes = TestHelpers.ReadAllBytes(stream);
            CollectionAssert.AreEqual(TestHelpers.Encode("caf\u00e9", RegExCodePage.Utf8), bytes);
        }

        // windows-1252 maps 0x80 -> U+20AC (EURO SIGN), which differs from Latin-1's
        // identity mapping, so these exercise the SBCS table lookup (not a pass-through).
        private const int Windows1252 = 1252;

        // windows-1251 (Cyrillic): high bytes map to the Unicode Cyrillic block.
        private const int Windows1251 = 1251;

        [TestMethod]
        public void Transcode_Windows1252ToString()
        {
            var bytes = new byte[] { 0x41, 0x80, 0xE9 }; // "A", U+20AC, U+00E9

            var result = RegEx.Transcode(new RegExInput(bytes, Windows1252));

            Assert.AreEqual("A\u20AC\u00E9", result);
        }

        [TestMethod]
        public void TranscodeTo_StringToWindows1252()
        {
            using var stream = RegEx.CreateMemoryStream();

            RegEx.TranscodeTo("A\u20AC\u00E9", stream, Windows1252);

            var bytes = TestHelpers.ReadAllBytes(stream);
            CollectionAssert.AreEqual(new byte[] { 0x41, 0x80, 0xE9 }, bytes);
        }

        [TestMethod]
        public void Transcode_Windows1251ToString()
        {
            var bytes = new byte[] { 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2 }; // "Привет"

            var result = RegEx.Transcode(new RegExInput(bytes, Windows1251));

            Assert.AreEqual("\u041F\u0440\u0438\u0432\u0435\u0442", result);
        }

        [TestMethod]
        public void TranscodeTo_StringToWindows1251()
        {
            using var stream = RegEx.CreateMemoryStream();

            RegEx.TranscodeTo("\u041F\u0440\u0438\u0432\u0435\u0442", stream, Windows1251);

            var bytes = TestHelpers.ReadAllBytes(stream);
            CollectionAssert.AreEqual(new byte[] { 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2 }, bytes);
        }

        [TestMethod]
        public void Transcode_Windows1252RoundTrips()
        {
            // Decode SBCS -> string, then encode string -> SBCS and confirm the bytes survive.
            var original = new byte[] { 0x41, 0x80, 0x93, 0x94, 0xE9 }; // "A", U+20AC, U+201C, U+201D, U+00E9

            var text = RegEx.Transcode(new RegExInput(original, Windows1252));

            using var stream = RegEx.CreateMemoryStream();
            RegEx.TranscodeTo(text, stream, Windows1252);

            CollectionAssert.AreEqual(original, TestHelpers.ReadAllBytes(stream));
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
                m => m.Text);

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
