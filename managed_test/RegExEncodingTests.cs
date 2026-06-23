namespace UnicodeRegEx.Tests
{
    using System.Text;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;

    [TestClass]
    public class RegExEncodingTests
    {
        [TestMethod]
        public void FromCodePage_Utf8_ReturnsUtf8()
        {
            Assert.AreEqual(RegExCodePage.Utf8, RegExEncoding.FromCodePage(RegExCodePage.Utf8).CodePage);
        }

        [TestMethod]
        public void FromCodePage_Utf16LE_ReturnsUnicode()
        {
            var encoding = RegExEncoding.FromCodePage(RegExCodePage.Utf16LE);
            Assert.AreEqual(RegExCodePage.Utf16LE, encoding.CodePage);
            Assert.AreEqual(Encoding.Unicode.CodePage, encoding.CodePage);
        }

        [TestMethod]
        public void FromCodePage_Utf16BE_ReturnsBigEndianUnicode()
        {
            var encoding = RegExEncoding.FromCodePage(RegExCodePage.Utf16BE);
            Assert.AreEqual(RegExCodePage.Utf16BE, encoding.CodePage);
            Assert.AreEqual(Encoding.BigEndianUnicode.CodePage, encoding.CodePage);
        }

        [TestMethod]
        public void FromCodePage_Latin1_ReturnsLatin1()
        {
            Assert.AreEqual(RegExCodePage.Latin1, RegExEncoding.FromCodePage(RegExCodePage.Latin1).CodePage);
        }

        [TestMethod]
        public void FromCodePage_Latin1_ReturnsTheCachedInstance()
        {
            // The Latin1 arm returns the shared cached instance, not a fresh GetEncoding each call.
            Assert.AreSame(RegExEncoding.Latin1, RegExEncoding.FromCodePage(RegExCodePage.Latin1));
        }

        [TestMethod]
        public void FromCodePage_ArbitraryCodePage_FallsBackToGetEncoding()
        {
            var encoding = RegExEncoding.FromCodePage(1252);
            Assert.AreEqual(1252, encoding.CodePage);
        }

        [TestMethod]
        public void Latin1_IsCached_ReturnsSameInstance()
        {
            Assert.AreSame(RegExEncoding.Latin1, RegExEncoding.Latin1);
        }

        [TestMethod]
        public void Latin1_RoundTripsHighBytes()
        {
            // Latin-1 is an identity map for 0x00-0xFF: byte value == code point.
            var bytes = new byte[] { 0xE1, 0xE9, 0xED }; // á é í in Latin-1
            Assert.AreEqual("\u00e1\u00e9\u00ed", RegExEncoding.Latin1.GetString(bytes));
        }

        [TestMethod]
        public void FromCodePage_Utf8_DecodesMultibyte()
        {
            var bytes = Encoding.UTF8.GetBytes("caf\u00e9");
            Assert.AreEqual("caf\u00e9", RegExEncoding.FromCodePage(RegExCodePage.Utf8).GetString(bytes));
        }
    }
}
