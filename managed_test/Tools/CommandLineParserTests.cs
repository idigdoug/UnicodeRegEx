namespace UnicodeRegEx.Tests.Tools
{
    using System.Collections.Generic;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Settings;

    [TestClass]
    public class CommandLineParserTests
    {
        private static (SearchSettings Settings, CommandLineParseResult Result, List<string> Errors) Run(params string[] args)
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            var result = CommandLine.Parse(args, settings, errors);
            return (settings, result, errors);
        }

        [TestMethod]
        public void Positionals_AreCollectedInOrder()
        {
            var (_, result, errors) = Run("pattern", "a.txt", "b.txt");
            CollectionAssert.AreEqual(new List<string>(), errors);
            CollectionAssert.AreEqual(new List<string> { "pattern", "a.txt", "b.txt" }, result.Positionals);
        }

        [TestMethod]
        public void ShortFlag_SetsBoolean()
        {
            var (settings, _, errors) = Run("-i", "p", "x");
            CollectionAssert.AreEqual(new List<string>(), errors);
            Assert.IsTrue(settings.IgnoreCase.Value);
        }

        [TestMethod]
        public void LongFlag_SetsBoolean()
        {
            var (settings, _, _) = Run("--ignore-case", "p", "x");
            Assert.IsTrue(settings.IgnoreCase.Value);
        }

        [TestMethod]
        public void Flag_DefaultsOff_WhenAbsent()
        {
            var (settings, _, _) = Run("p", "x");
            Assert.IsFalse(settings.IgnoreCase.Value);
        }

        [TestMethod]
        public void LongValue_SpaceSeparated()
        {
            var (settings, _, errors) = Run("--encoding", "utf8", "p", "x");
            CollectionAssert.AreEqual(new List<string>(), errors);
            Assert.AreEqual(RegExCodePage.Utf8, settings.Encoding.Value);
        }

        [TestMethod]
        public void LongValue_EqualsSeparated()
        {
            var (settings, _, _) = Run("--encoding=1252", "p", "x");
            Assert.AreEqual(1252, settings.Encoding.Value);
        }

        [TestMethod]
        public void Value_TakesTheReplaceTemplate()
        {
            var (settings, _, _) = Run("--replace", "X", "p", "x");
            Assert.AreEqual("X", settings.Replace.Value);
        }

        [TestMethod]
        public void DoubleDash_EndsOptionProcessing()
        {
            // After "--", a token that looks like a flag becomes a positional.
            var (settings, result, _) = Run("p", "--", "-i", "x");
            Assert.IsFalse(settings.IgnoreCase.Value);
            CollectionAssert.AreEqual(new List<string> { "p", "-i", "x" }, result.Positionals);
        }

        [TestMethod]
        public void LoneDash_IsAPositional()
        {
            var (_, result, _) = Run("p", "-");
            CollectionAssert.AreEqual(new List<string> { "p", "-" }, result.Positionals);
        }

        [TestMethod]
        [DataRow("--help")]
        [DataRow("-h")]
        [DataRow("-?")]
        public void HelpToken_RequestsHelp(string token)
        {
            var (_, result, _) = Run(token);
            Assert.IsTrue(result.HelpRequested);
        }

        [TestMethod]
        public void UnknownLongOption_ReportsError()
        {
            var (_, _, errors) = Run("--bogus", "p", "x");
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "--bogus");
        }

        [TestMethod]
        public void UnknownShortOption_ReportsError()
        {
            var (_, _, errors) = Run("-z", "p", "x");
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "-z");
        }

        [TestMethod]
        public void MissingValue_ReportsError()
        {
            var (_, _, errors) = Run("--encoding");
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "encoding");
        }

        [TestMethod]
        public void BadValue_ReportsError()
        {
            var (_, _, errors) = Run("--encoding", "not-a-codepage", "p", "x");
            Assert.AreEqual(1, errors.Count);
        }

        [TestMethod]
        public void OptionsAndPositionals_MayInterleave()
        {
            var (settings, result, errors) = Run("p", "-i", "a.txt", "--encoding", "utf8", "b.txt");
            CollectionAssert.AreEqual(new List<string>(), errors);
            Assert.IsTrue(settings.IgnoreCase.Value);
            Assert.AreEqual(RegExCodePage.Utf8, settings.Encoding.Value);
            CollectionAssert.AreEqual(new List<string> { "p", "a.txt", "b.txt" }, result.Positionals);
        }
    }
}
