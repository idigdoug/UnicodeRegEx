namespace UnicodeRegEx.Tests.Tools
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx.Tools;

    [TestClass]
    public class GlobToRegexTests
    {
        [TestMethod]
        public void Null_ReturnsNull()
        {
            Assert.IsNull(GlobToRegex.Compile(null));
        }

        [TestMethod]
        public void Empty_ReturnsNull()
        {
            Assert.IsNull(GlobToRegex.Compile(string.Empty));
        }

        [TestMethod]
        public void OnlySeparators_ReturnsNull()
        {
            Assert.IsNull(GlobToRegex.Compile(";;;"));
        }

        [TestMethod]
        public void Star_MatchesAnyRunWithinAName()
        {
            var regex = GlobToRegex.Compile("*.cs");
            Assert.IsNotNull(regex);
            Assert.IsTrue(regex!.IsMatch("Program.cs"));
            Assert.IsTrue(regex.IsMatch(".cs"));
            Assert.IsFalse(regex.IsMatch("Program.csx"));
            Assert.IsFalse(regex.IsMatch("Program.cs.bak"));
        }

        [TestMethod]
        public void Question_MatchesExactlyOneCharacter()
        {
            var regex = GlobToRegex.Compile("a?c");
            Assert.IsNotNull(regex);
            Assert.IsTrue(regex!.IsMatch("abc"));
            Assert.IsTrue(regex.IsMatch("a-c"));
            Assert.IsFalse(regex.IsMatch("ac"));
            Assert.IsFalse(regex.IsMatch("abbc"));
        }

        [TestMethod]
        public void Wildcards_DoNotCrossPathSeparators()
        {
            var regex = GlobToRegex.Compile("*.cs");
            Assert.IsNotNull(regex);
            // The whole name is anchored, and * does not cross / or \, so a path does not match.
            Assert.IsFalse(regex!.IsMatch("src/Program.cs"));
            Assert.IsFalse(regex.IsMatch("src\\Program.cs"));
        }

        [TestMethod]
        public void SemicolonList_MatchesAnyAlternative()
        {
            var regex = GlobToRegex.Compile("*.cs;*.txt");
            Assert.IsNotNull(regex);
            Assert.IsTrue(regex!.IsMatch("a.cs"));
            Assert.IsTrue(regex.IsMatch("b.txt"));
            Assert.IsFalse(regex.IsMatch("c.md"));
        }

        [TestMethod]
        public void List_IgnoresWhitespaceAndEmptyEntries()
        {
            var regex = GlobToRegex.Compile(" *.cs ; ; *.txt ");
            Assert.IsNotNull(regex);
            Assert.IsTrue(regex!.IsMatch("a.cs"));
            Assert.IsTrue(regex.IsMatch("b.txt"));
        }

        [TestMethod]
        public void Matching_IsCaseInsensitive()
        {
            var regex = GlobToRegex.Compile("*.CS");
            Assert.IsNotNull(regex);
            Assert.IsTrue(regex!.IsMatch("Program.cs"));
            Assert.IsTrue(regex.IsMatch("PROGRAM.CS"));
        }

        [TestMethod]
        public void RegexMetacharactersInPattern_AreTreatedLiterally()
        {
            // A '.' in the glob is a literal dot, not a regex wildcard.
            var regex = GlobToRegex.Compile("a.c");
            Assert.IsNotNull(regex);
            Assert.IsTrue(regex!.IsMatch("a.c"));
            Assert.IsFalse(regex.IsMatch("abc"));
        }
    }
}
