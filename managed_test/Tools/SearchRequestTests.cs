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
        public void ApplySettings_IgnoreCase_FoldsIntoSyntaxFlags()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "-i", "p", "x" }, settings, errors);

            var request = new SearchRequest();
            request.ApplySettings(settings);

            Assert.IsTrue(request.SyntaxFlags.HasFlag(RegExSyntaxFlags.ICase));
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

        [TestMethod]
        public void ComposeSyntaxFlags_DefaultPerl_ForcesDotNotNewline()
        {
            // DotAll is authoritative: with it off (the default), the perl group gets no_mod_s so "."
            // deterministically does not match a newline.
            Assert.AreEqual(
                RegExSyntaxFlags.Perl | RegExSyntaxFlags.NoModS,
                SearchRequest.ComposeSyntaxFlags(RegExSyntaxFlags.Perl));
        }

        [TestMethod]
        public void ComposeSyntaxFlags_IgnoreCase_AddsICase()
        {
            Assert.AreEqual(
                RegExSyntaxFlags.Perl | RegExSyntaxFlags.ICase | RegExSyntaxFlags.NoModS,
                SearchRequest.ComposeSyntaxFlags(RegExSyntaxFlags.Perl, ignoreCase: true));
        }

        [TestMethod]
        public void ComposeSyntaxFlags_CollateOnPerl_AddsCollate()
        {
            var flags = SearchRequest.ComposeSyntaxFlags(RegExSyntaxFlags.Perl, collate: true);
            Assert.IsTrue(flags.HasFlag(RegExSyntaxFlags.Collate));
        }

        [TestMethod]
        public void ComposeSyntaxFlags_BasicWithoutCollate_ClearsBundledCollateBit()
        {
            // Basic bundles collate in its POSIX definition; the composer treats collate as its own axis,
            // so with collate off it compiles Basic without the collate bit.
            var flags = SearchRequest.ComposeSyntaxFlags(RegExSyntaxFlags.Basic);
            Assert.IsFalse(flags.HasFlag(RegExSyntaxFlags.Collate));
        }

        [TestMethod]
        public void ComposeSyntaxFlags_BasicWithCollate_KeepsCollateBit()
        {
            var flags = SearchRequest.ComposeSyntaxFlags(RegExSyntaxFlags.Basic, collate: true);
            Assert.IsTrue(flags.HasFlag(RegExSyntaxFlags.Collate));
        }

        [TestMethod]
        public void ComposeSyntaxFlags_PerlModifiers_SetTheirBits()
        {
            var flags = SearchRequest.ComposeSyntaxFlags(
                RegExSyntaxFlags.Perl, dotAll: true, freeSpacing: true, multilineAnchors: false);

            Assert.IsTrue(flags.HasFlag(RegExSyntaxFlags.ModS));
            Assert.IsFalse(flags.HasFlag(RegExSyntaxFlags.NoModS));
            Assert.IsTrue(flags.HasFlag(RegExSyntaxFlags.ModX));
            Assert.IsTrue(flags.HasFlag(RegExSyntaxFlags.NoModM));
        }

        [TestMethod]
        public void ComposeSyntaxFlags_PerlModifiers_ApplyToExtended()
        {
            // Extended is in the perl syntax group, so the modifiers apply there too.
            var flags = SearchRequest.ComposeSyntaxFlags(RegExSyntaxFlags.Extended, dotAll: true);
            Assert.IsTrue(flags.HasFlag(RegExSyntaxFlags.ModS));
        }

        [TestMethod]
        public void ComposeSyntaxFlags_PerlModifiers_SuppressedForBasicGroup()
        {
            // In the basic group bits 10-13 alias to unrelated options, so the composer does not set them.
            var flags = SearchRequest.ComposeSyntaxFlags(
                RegExSyntaxFlags.Basic, dotAll: true, freeSpacing: true, multilineAnchors: false);

            Assert.IsFalse(flags.HasFlag(RegExSyntaxFlags.ModS));
            Assert.IsFalse(flags.HasFlag(RegExSyntaxFlags.NoModS));
            Assert.IsFalse(flags.HasFlag(RegExSyntaxFlags.ModX));
            Assert.IsFalse(flags.HasFlag(RegExSyntaxFlags.NoModM));
        }

        [TestMethod]
        public void SetSyntaxFlags_ResultIsCarriedByClone()
        {
            var original = new SearchRequest();
            original.SetSyntaxFlags(
                RegExSyntaxFlags.Perl, collate: true, dotAll: true, freeSpacing: true, multilineAnchors: false);

            var copy = original.Clone();

            Assert.AreEqual(original.SyntaxFlags, copy.SyntaxFlags);
            Assert.AreEqual(
                SearchRequest.ComposeSyntaxFlags(
                    RegExSyntaxFlags.Perl, collate: true, dotAll: true, freeSpacing: true, multilineAnchors: false),
                copy.SyntaxFlags);
        }

        [TestMethod]
        public void MatchFlags_DefaultsToDefault()
        {
            Assert.AreEqual(RegExMatchFlags.Default, new SearchRequest().MatchFlags);
        }

        [TestMethod]
        public void MatchFlags_IsCarriedByClone()
        {
            var original = new SearchRequest { MatchFlags = RegExMatchFlags.NotBol | RegExMatchFlags.NotBob };

            var copy = original.Clone();

            Assert.AreEqual(RegExMatchFlags.NotBol | RegExMatchFlags.NotBob, copy.MatchFlags);
        }
    }
}
