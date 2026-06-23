namespace UnicodeRegEx.Tests.Tools
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;

    [TestClass]
    public class CodePagesTests
    {
        [TestMethod]
        [DataRow("utf8", RegExCodePage.Utf8)]
        [DataRow("utf-8", RegExCodePage.Utf8)]
        [DataRow("UTF8", RegExCodePage.Utf8)]
        [DataRow("utf16", RegExCodePage.Utf16LE)]
        [DataRow("utf-16", RegExCodePage.Utf16LE)]
        [DataRow("utf16le", RegExCodePage.Utf16LE)]
        [DataRow("utf-16le", RegExCodePage.Utf16LE)]
        [DataRow("utf16be", RegExCodePage.Utf16BE)]
        [DataRow("utf-16be", RegExCodePage.Utf16BE)]
        [DataRow("latin1", RegExCodePage.Latin1)]
        [DataRow("iso-8859-1", RegExCodePage.Latin1)]
        [DataRow("iso8859-1", RegExCodePage.Latin1)]
        [DataRow("acp", RegExCodePage.SystemDefault)]
        [DataRow("ansi", RegExCodePage.SystemDefault)]
        public void TryParse_KnownAliases_ResolveCaseInsensitively(string spec, int expected)
        {
            Assert.IsTrue(CodePages.TryParse(spec, out var codePage));
            Assert.AreEqual(expected, codePage);
        }

        [TestMethod]
        public void TryParse_SurroundingWhitespace_IsTrimmed()
        {
            Assert.IsTrue(CodePages.TryParse("  utf8  ", out var codePage));
            Assert.AreEqual(RegExCodePage.Utf8, codePage);
        }

        [TestMethod]
        public void TryParse_NumericString_IsAccepted()
        {
            Assert.IsTrue(CodePages.TryParse("1252", out var codePage));
            Assert.AreEqual(1252, codePage);
        }

        [TestMethod]
        public void TryParse_Zero_IsAccepted()
        {
            Assert.IsTrue(CodePages.TryParse("0", out var codePage));
            Assert.AreEqual(0, codePage);
        }

        [TestMethod]
        [DataRow("nonsense")]
        [DataRow("-1")]
        [DataRow("utf99")]
        [DataRow("")]
        public void TryParse_Unknown_ReturnsFalse(string spec)
        {
            Assert.IsFalse(CodePages.TryParse(spec, out _));
        }

        [TestMethod]
        [DataRow(RegExCodePage.Utf8, "utf8")]
        [DataRow(RegExCodePage.Utf16LE, "utf16le")]
        [DataRow(RegExCodePage.Utf16BE, "utf16be")]
        [DataRow(RegExCodePage.Latin1, "latin1")]
        [DataRow(RegExCodePage.SystemDefault, "acp")]
        public void GetName_KnownCodePages_ReturnCanonicalAlias(int codePage, string expected)
        {
            Assert.AreEqual(expected, CodePages.GetName(codePage));
        }

        [TestMethod]
        public void GetName_UnknownCodePage_ReturnsNumber()
        {
            Assert.AreEqual("1252", CodePages.GetName(1252));
        }

        [TestMethod]
        public void GetName_CanonicalAlias_RoundTripsThroughTryParse()
        {
            foreach (var codePage in new[] { RegExCodePage.Utf8, RegExCodePage.Utf16LE, RegExCodePage.Utf16BE, RegExCodePage.Latin1 })
            {
                Assert.IsTrue(CodePages.TryParse(CodePages.GetName(codePage), out var roundTripped));
                Assert.AreEqual(codePage, roundTripped);
            }
        }

        [TestMethod]
        public void ResolveDefault_NonSentinel_IsUnchanged()
        {
            Assert.AreEqual(RegExCodePage.Utf8, CodePages.ResolveDefault(RegExCodePage.Utf8));
            Assert.AreEqual(1252, CodePages.ResolveDefault(1252));
        }

        [TestMethod]
        public void ResolveDefault_Sentinel_ResolvesToConcreteAnsiCodePage()
        {
            // CP_ACP (0) resolves to the machine's real ANSI code page, which is never the sentinel.
            var resolved = CodePages.ResolveDefault(RegExCodePage.SystemDefault);
            Assert.AreNotEqual(RegExCodePage.SystemDefault, resolved);
            Assert.IsTrue(resolved > 0);
        }

        [TestMethod]
        public void IsSupported_Utf8_IsTrue()
        {
            Assert.IsTrue(CodePages.IsSupported(RegExCodePage.Utf8));
        }
    }
}
