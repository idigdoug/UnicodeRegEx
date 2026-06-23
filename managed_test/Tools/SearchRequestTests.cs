namespace UnicodeRegEx.Tests.Tools
{
    using System.Collections.Generic;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;

    [TestClass]
    public class SearchRequestTests
    {
        private static SearchRequest Valid()
        {
            var request = new SearchRequest { Pattern = "x" };
            request.Paths.Add(".");
            return request;
        }

        [TestMethod]
        public void Validate_ValidRequest_HasNoProblems()
        {
            CollectionAssert.AreEqual(new List<SearchRequestProblem>(), new List<SearchRequestProblem>(Valid().Validate()));
        }

        [TestMethod]
        public void Validate_EmptyPattern_ReportsPatternRequired()
        {
            var request = new SearchRequest();
            request.Paths.Add(".");
            CollectionAssert.Contains(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.PatternRequired);
        }

        [TestMethod]
        public void Validate_NoPaths_ReportsPathRequired()
        {
            var request = new SearchRequest { Pattern = "x" };
            CollectionAssert.Contains(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.PathRequired);
        }

        [TestMethod]
        public void Validate_ApplyWithoutReplaceVerb_ReportsApplyRequiresReplace()
        {
            var request = Valid();
            request.Apply = true;
            request.Verb = SearchVerb.Search;
            CollectionAssert.Contains(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.ApplyRequiresReplace);
        }

        [TestMethod]
        public void Validate_ApplyWithReplaceVerb_IsAllowed()
        {
            var request = Valid();
            request.Apply = true;
            request.Verb = SearchVerb.Replace;
            request.ReplaceTemplate = "X";
            CollectionAssert.DoesNotContain(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.ApplyRequiresReplace);
        }

        [TestMethod]
        public void Verb_IsIndependentOfTemplatePresence()
        {
            // The whole point of the verb decoupling: a present-but-empty template under Search must
            // NOT be treated as a replace (a GUI's always-present empty box). The verb is explicit,
            // never derived from whether ReplaceTemplate is non-null.
            var request = Valid();
            request.ReplaceTemplate = string.Empty;
            request.Verb = SearchVerb.Search;
            // Apply requires the Replace verb, so a present-but-empty template under Search does not
            // satisfy it — proving the verb, not template presence, drives replacement.
            request.Apply = true;
            CollectionAssert.Contains(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.ApplyRequiresReplace);

            request.Verb = SearchVerb.Replace;
            CollectionAssert.DoesNotContain(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.ApplyRequiresReplace);
        }

        [TestMethod]
        public void ApplySettings_ReplacePresent_SelectsReplaceVerb()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--replace", "X", "p", "x" }, settings, errors);
            CollectionAssert.AreEqual(new List<string>(), errors);

            var request = new SearchRequest();
            request.ApplySettings(settings);

            Assert.AreEqual(SearchVerb.Replace, request.Verb);
            Assert.AreEqual("X", request.ReplaceTemplate);
        }

        [TestMethod]
        public void ApplySettings_NoReplace_SelectsSearchVerb()
        {
            var settings = new SearchSettings();
            var request = new SearchRequest();
            request.ApplySettings(settings);

            Assert.AreEqual(SearchVerb.Search, request.Verb);
            Assert.IsNull(request.ReplaceTemplate);
        }

        [TestMethod]
        public void ApplySettings_CopiesSyntaxFlavor()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "-F", "p", "x" }, settings, errors);

            var request = new SearchRequest();
            request.ApplySettings(settings);

            Assert.AreEqual(RegExSyntaxFlags.Literal, request.SyntaxFlags);
        }

        [TestMethod]
        public void DefaultCodePage_ResolvesSentinelIntoResolvedDefault()
        {
            var request = new SearchRequest { DefaultCodePage = RegExCodePage.SystemDefault };
            Assert.AreNotEqual(RegExCodePage.SystemDefault, request.ResolvedDefaultCodePage);
            Assert.IsTrue(request.ResolvedDefaultCodePage > 0);
        }

        [TestMethod]
        public void Clone_ProducesIndependentPathsList()
        {
            var original = Valid();
            original.Verb = SearchVerb.Replace;
            original.ReplaceTemplate = "X";

            var copy = original.Clone();
            copy.Paths.Add("extra");

            Assert.AreEqual(1, original.Paths.Count, "original's list must not be affected by the clone");
            Assert.AreEqual(2, copy.Paths.Count);
            Assert.AreEqual(SearchVerb.Replace, copy.Verb);
            Assert.AreEqual("X", copy.ReplaceTemplate);
        }
    }
}
