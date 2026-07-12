namespace UnicodeRegEx.Tests.Tools
{
    using System.Linq;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx.Tools;

    [TestClass]
    public class OpenWithCommandTests
    {
        // Substitution

        [TestMethod]
        public void Substitute_AllTokens()
        {
            var result = OpenWithCommand.Substitute("app +$L:$C \"$F\"", @"C:\a\b.txt", 12, 5);
            Assert.AreEqual("app +12:5 \"C:\\a\\b.txt\"", result);
        }

        [TestMethod]
        public void Substitute_DollarEscape()
        {
            Assert.AreEqual("cost is $5", OpenWithCommand.Substitute("cost is $$5", "f", 1, 1));
        }

        [TestMethod]
        public void Substitute_AdjacentTokens()
        {
            Assert.AreEqual("3,7", OpenWithCommand.Substitute("$L,$C", "f", 3, 7));
        }

        [TestMethod]
        public void Substitute_UnknownTokenKeptLiteral()
        {
            // $X is not a known token, so the '$' and 'X' are both preserved.
            Assert.AreEqual("$Xtail", OpenWithCommand.Substitute("$Xtail", "f", 1, 1));
        }

        [TestMethod]
        public void Substitute_TrailingDollarKeptLiteral()
        {
            Assert.AreEqual("a$", OpenWithCommand.Substitute("a$", "f", 1, 1));
        }

        [TestMethod]
        public void Substitute_SubstitutedValueDoesNotRetrigger()
        {
            // The file path itself contains "$L"; it must not be re-substituted.
            Assert.AreEqual("$L", OpenWithCommand.Substitute("$F", "$L", 9, 9));
        }

        // Quote-aware splitting

        [TestMethod]
        public void Split_SimpleTokens()
        {
            CollectionAssert.AreEqual(new[] { "app", "a", "b" }, OpenWithCommand.SplitArguments("app a b").ToArray());
        }

        [TestMethod]
        public void Split_QuotedPathWithSpaces()
        {
            CollectionAssert.AreEqual(
                new[] { "gvim.exe", "+12", "C:\\Program Files\\x.txt" },
                OpenWithCommand.SplitArguments("gvim.exe +12 \"C:\\Program Files\\x.txt\"").ToArray());
        }

        [TestMethod]
        public void Split_EmptyQuotedToken()
        {
            CollectionAssert.AreEqual(new[] { "app", string.Empty, "b" }, OpenWithCommand.SplitArguments("app \"\" b").ToArray());
        }

        [TestMethod]
        public void Split_BlankIsEmpty()
        {
            Assert.AreEqual(0, OpenWithCommand.SplitArguments("   ").Count);
        }

        [TestMethod]
        public void DefaultTools_IncludesNotepad()
        {
            var tools = OpenWithCommand.DefaultTools();
            Assert.AreEqual(1, tools.Count);
            Assert.AreEqual("Open with Notepad", tools[0].Name);
            StringAssert.Contains(tools[0].CommandLine, "$F");
        }
    }
}
