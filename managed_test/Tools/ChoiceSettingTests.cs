namespace UnicodeRegEx.Tests.Tools
{
    using System.Collections.Generic;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Settings;

    [TestClass]
    public class ChoiceSettingTests
    {
        private static SearchSettings Parse(params string[] args)
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            var result = CommandLine.Parse(args, settings, errors);
            Assert.IsFalse(result.HelpRequested, "did not expect help");
            CollectionAssert.AreEqual(new List<string>(), errors, "did not expect parse errors");
            return settings;
        }

        [TestMethod]
        public void Syntax_DefaultsToPerl()
        {
            var settings = new SearchSettings();
            Assert.AreEqual(RegExSyntaxFlags.Perl, settings.Syntax.Value);
        }

        [TestMethod]
        [DataRow("-E", RegExSyntaxFlags.Extended)]
        [DataRow("-F", RegExSyntaxFlags.Literal)]
        [DataRow("-G", RegExSyntaxFlags.Basic)]
        [DataRow("-P", RegExSyntaxFlags.Perl)]
        public void Syntax_ShortFlag_SelectsFlavor(string flag, RegExSyntaxFlags expected)
        {
            var settings = Parse(flag, "pattern", "path");
            Assert.AreEqual(expected, settings.Syntax.Value);
        }

        [TestMethod]
        [DataRow("--extended-regexp", RegExSyntaxFlags.Extended)]
        [DataRow("--fixed-strings", RegExSyntaxFlags.Literal)]
        [DataRow("--basic-regexp", RegExSyntaxFlags.Basic)]
        [DataRow("--perl-regexp", RegExSyntaxFlags.Perl)]
        public void Syntax_LongFlag_SelectsFlavor(string flag, RegExSyntaxFlags expected)
        {
            var settings = Parse(flag, "pattern", "path");
            Assert.AreEqual(expected, settings.Syntax.Value);
        }

        [TestMethod]
        public void Syntax_ConflictingFlags_LastWins()
        {
            Assert.AreEqual(RegExSyntaxFlags.Perl, Parse("-F", "-P", "p", "x").Syntax.Value);
            Assert.AreEqual(RegExSyntaxFlags.Literal, Parse("-P", "-F", "p", "x").Syntax.Value);
            Assert.AreEqual(RegExSyntaxFlags.Basic, Parse("-E", "-F", "-G", "p", "x").Syntax.Value);
        }

        [TestMethod]
        public void Syntax_ChoiceFlag_DoesNotConsumeNextArgument()
        {
            // -F is valueless; the following token must remain a positional (the pattern), not be
            // swallowed as -F's value.
            var settings = new SearchSettings();
            var errors = new List<string>();
            var result = CommandLine.Parse(new[] { "-F", "mypattern", "mypath" }, settings, errors);

            CollectionAssert.AreEqual(new List<string>(), errors);
            Assert.AreEqual(RegExSyntaxFlags.Literal, settings.Syntax.Value);
            CollectionAssert.AreEqual(new List<string> { "mypattern", "mypath" }, result.Positionals);
        }

        [TestMethod]
        public void Syntax_CanonicalLongName_IsNotACommandLineToken()
        {
            // The canonical config key "syntax" is not itself a CLI flag.
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--syntax", "extended", "p", "x" }, settings, errors);

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "--syntax");
            // Unrecognized: flavor stays at the default.
            Assert.AreEqual(RegExSyntaxFlags.Perl, settings.Syntax.Value);
        }

        [TestMethod]
        [DataRow("extended", RegExSyntaxFlags.Extended)]
        [DataRow("fixed", RegExSyntaxFlags.Literal)]
        [DataRow("basic", RegExSyntaxFlags.Basic)]
        [DataRow("perl", RegExSyntaxFlags.Perl)]
        [DataRow("EXTENDED", RegExSyntaxFlags.Extended)]
        public void Syntax_ApplyOverlay_ResolvesCanonicalName(string name, RegExSyntaxFlags expected)
        {
            // Simulates the config layer (AppConfigSource) overlaying syntax=<name> by long name.
            var settings = new SearchSettings();
            var errors = new List<string>();
            var overlay = new[] { new KeyValuePair<string, string?>("syntax", name) };
            settings.ApplyOverlay(overlay, "config", errors);

            CollectionAssert.AreEqual(new List<string>(), errors);
            Assert.AreEqual(expected, settings.Syntax.Value);
        }

        [TestMethod]
        public void Syntax_ApplyOverlay_UnknownName_ReportsError()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            var overlay = new[] { new KeyValuePair<string, string?>("syntax", "nonsense") };
            settings.ApplyOverlay(overlay, "config", errors);

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "config");
        }

        [TestMethod]
        public void Help_RendersEachChoiceFlag_AndMarksDefault()
        {
            var help = HelpFormatter.Format("usage: test", new SearchSettings());

            StringAssert.Contains(help, "-E, --extended-regexp");
            StringAssert.Contains(help, "-F, --fixed-strings");
            StringAssert.Contains(help, "-G, --basic-regexp");
            StringAssert.Contains(help, "-P, --perl-regexp");
            // The default flavor line is marked.
            StringAssert.Contains(help, "Perl/ECMAScript-compatible regular expressions. [default]");
        }
    }
}
