namespace UnicodeRegEx.Tests.Tools
{
    using System.Collections.Generic;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx.Tools;

    [TestClass]
    public class GlobFilterSetTests
    {
        // Compiles with grep's filename default (include unless the first filter is an include), which is
        // what the filename call site computes; directory-specific behavior is covered separately.
        private static GlobFilterSet? Compile(params GlobFilter[] filters) =>
            GlobFilterSet.Compile(filters, GrepDefaultInclude(filters));

        private static bool GrepDefaultInclude(IReadOnlyList<GlobFilter> filters) =>
            filters.Count == 0 || filters[0].Kind != FilterKind.Include;

        private static GlobFilter Include(string glob) => new GlobFilter(FilterKind.Include, glob);

        private static GlobFilter Exclude(string glob) => new GlobFilter(FilterKind.Exclude, glob);

        [TestMethod]
        public void Empty_ReturnsNull()
        {
            Assert.IsNull(GlobFilterSet.Compile(new List<GlobFilter>(), defaultIncludeWhenNoMatch: true));
        }

        [TestMethod]
        public void IncludeOnly_FirstIsInclude_DefaultsToExclude()
        {
            var set = Compile(Include("*.cs"))!;

            Assert.IsTrue(set.ShouldInclude("a.cs"));
            Assert.IsFalse(set.ShouldInclude("a.txt")); // no match, first is Include => default exclude
        }

        [TestMethod]
        public void FirstIsExclude_DefaultsToInclude()
        {
            // grep's surprising case: nothing matches foo.txt, and the first filter is Exclude,
            // so it is included.
            var set = Compile(Exclude("*.tmp"), Include("*.cs"))!;

            Assert.IsTrue(set.ShouldInclude("foo.txt"));
            Assert.IsFalse(set.ShouldInclude("foo.tmp")); // matches the exclude
            Assert.IsTrue(set.ShouldInclude("foo.cs"));   // matches the include
        }

        [TestMethod]
        public void LastMatchingFilterWins()
        {
            var set = Compile(Include("*.cs"), Exclude("foo.*"), Include("foo.cs"))!;

            Assert.IsTrue(set.ShouldInclude("foo.cs"));  // last match is the trailing include
            Assert.IsFalse(set.ShouldInclude("foo.bak")); // last match is the exclude
            Assert.IsTrue(set.ShouldInclude("bar.cs"));  // only the first include matches
        }

        [TestMethod]
        public void SameKindRun_IsLosslessVsIndividualEvaluation()
        {
            // A run of includes followed by a run of excludes must behave exactly as if each filter were
            // evaluated one at a time (the collapse is order-preserving within a run).
            var filters = new[] { Include("*.cs"), Include("*.h"), Exclude("*_test.*"), Exclude("moc_*") };
            var set = GlobFilterSet.Compile(filters, GrepDefaultInclude(filters))!;

            Assert.IsTrue(set.ShouldInclude("a.cs"));
            Assert.IsTrue(set.ShouldInclude("a.h"));
            Assert.IsFalse(set.ShouldInclude("a_test.cs")); // excluded by *_test.*
            Assert.IsFalse(set.ShouldInclude("moc_widget.cpp")); // excluded by moc_*
            Assert.IsFalse(set.ShouldInclude("a.o")); // nothing matches; first is Include => default exclude
        }

        [TestMethod]
        public void Wildcards_MatchCaseInsensitively()
        {
            var set = Compile(Include("*.CS"))!;

            Assert.IsTrue(set.ShouldInclude("Program.cs"));
        }

        [TestMethod]
        public void ReIncludeAfterExclude_RestoresInclusion()
        {
            var set = Compile(Include("*.cs"), Exclude("*.cs"), Include("keep.cs"))!;

            Assert.IsFalse(set.ShouldInclude("other.cs")); // include then exclude => excluded
            Assert.IsTrue(set.ShouldInclude("keep.cs"));   // trailing include wins
        }

        [TestMethod]
        public void DefaultIncludeWhenNoMatch_True_OverridesFirstIsIncludeRule()
        {
            // The directory case: a leading include must NOT default-exclude unmatched names.
            var filters = new[] { Include("src") };
            var set = GlobFilterSet.Compile(filters, defaultIncludeWhenNoMatch: true)!;

            Assert.IsTrue(set.ShouldInclude("src"));      // matches the include
            Assert.IsTrue(set.ShouldInclude("sibling"));  // no match, but default is include (descend)
        }

        [TestMethod]
        public void DefaultIncludeWhenNoMatch_True_ExcludeStillPrunesAndReincludeWorks()
        {
            var filters = new[] { Exclude("build"), Include("build_keep") };
            var set = GlobFilterSet.Compile(filters, defaultIncludeWhenNoMatch: true)!;

            Assert.IsFalse(set.ShouldInclude("build"));      // excluded
            Assert.IsTrue(set.ShouldInclude("build_keep"));  // re-included by a later filter
            Assert.IsTrue(set.ShouldInclude("anything"));    // unmatched => default include
        }
    }
}
