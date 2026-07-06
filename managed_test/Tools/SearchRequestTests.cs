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
        public void Settings_ApplyWithoutReplace_ReportsError()
        {
            // The --apply/--replace grammar rule lives in the settings layer now: --apply writes
            // replacements, so it needs --replace to have supplied a template.
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--apply", "p", "x" }, settings, errors);
            settings.Validate(errors);

            CollectionAssert.Contains(errors, "--apply requires --replace");
        }

        [TestMethod]
        public void Settings_ApplyWithReplace_IsAllowed()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--apply", "--replace", "X", "p", "x" }, settings, errors);
            settings.Validate(errors);

            CollectionAssert.AreEqual(new List<string>(), errors);
        }

        [TestMethod]
        public void Verb_IsExplicit_NotDerivedFromTemplate()
        {
            // The verb is the single authority on match-vs-apply; the template is pure data. A non-empty
            // template under the Match verb is a preview, never an implicit apply.
            var request = Valid();
            request.ReplaceTemplate = "X";
            request.Verb = SearchVerb.Match;

            // No request-level problem: an in-model request cannot represent "apply without a template"
            // (the verb is Match, and ReplaceTemplate is non-null anyway).
            CollectionAssert.AreEqual(new List<SearchRequestProblem>(), new List<SearchRequestProblem>(request.Validate()));
            Assert.AreEqual(SearchVerb.Match, request.Verb);
        }

        [TestMethod]
        public void ReplaceTemplate_DefaultsToEmpty_NotNull()
        {
            var request = new SearchRequest();
            Assert.AreEqual(string.Empty, request.ReplaceTemplate);
        }

        [TestMethod]
        public void ApplySettings_Apply_SelectsApplyVerb()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--apply", "--replace", "X", "p", "x" }, settings, errors);
            CollectionAssert.AreEqual(new List<string>(), errors);

            var request = new SearchRequest();
            request.ApplySettings(settings);

            Assert.AreEqual(SearchVerb.Apply, request.Verb);
            Assert.AreEqual("X", request.ReplaceTemplate);
        }

        [TestMethod]
        public void ApplySettings_ReplaceWithoutApply_SelectsMatchVerb()
        {
            // --replace without --apply is a preview: the Match verb with the template as data.
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--replace", "X", "p", "x" }, settings, errors);
            CollectionAssert.AreEqual(new List<string>(), errors);

            var request = new SearchRequest();
            request.ApplySettings(settings);

            Assert.AreEqual(SearchVerb.Match, request.Verb);
            Assert.AreEqual("X", request.ReplaceTemplate);
        }

        [TestMethod]
        public void ApplySettings_NoReplace_SelectsMatchVerb_WithEmptyTemplate()
        {
            var settings = new SearchSettings();
            var request = new SearchRequest();
            request.ApplySettings(settings);

            Assert.AreEqual(SearchVerb.Match, request.Verb);
            Assert.AreEqual(string.Empty, request.ReplaceTemplate);
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
        public void AddIncludeGlobs_SplitsSemicolonList_IntoIncludeFilters()
        {
            var request = new SearchRequest();
            request.AddIncludeFileGlobs(" *.cs ; ; *.txt ");

            Assert.AreEqual(2, request.FileNameFilters.Count);
            Assert.AreEqual(FilterKind.Include, request.FileNameFilters[0].Kind);
            Assert.AreEqual("*.cs", request.FileNameFilters[0].Glob);
            Assert.AreEqual(FilterKind.Include, request.FileNameFilters[1].Kind);
            Assert.AreEqual("*.txt", request.FileNameFilters[1].Glob);
        }

        [TestMethod]
        public void AddIncludeGlobs_Null_AddsNothing()
        {
            var request = new SearchRequest();
            request.AddIncludeFileGlobs(null);

            Assert.AreEqual(0, request.FileNameFilters.Count);
        }

        [TestMethod]
        public void ApplySettings_IncludeString_BecomesIncludeFilters()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--include", "*.cs;*.h", "p", "x" }, settings, errors);

            var request = new SearchRequest();
            request.ApplySettings(settings);

            Assert.AreEqual(2, request.FileNameFilters.Count);
            CollectionAssert.AreEqual(
                new[] { "*.cs", "*.h" },
                request.FileNameFilters.ConvertAll(f => f.Glob));
            Assert.IsTrue(request.FileNameFilters.TrueForAll(f => f.Kind == FilterKind.Include));
        }

        [TestMethod]
        public void AddExcludeDirGlobs_AppendsExcludeDirectoryFilters()
        {
            var request = new SearchRequest();
            request.AddExcludeDirGlobs("bin; ; obj");

            Assert.AreEqual(0, request.FileNameFilters.Count); // directory filters are a separate list
            Assert.AreEqual(2, request.DirectoryFilters.Count);
            CollectionAssert.AreEqual(
                new[] { "bin", "obj" },
                request.DirectoryFilters.ConvertAll(f => f.Glob));
            Assert.IsTrue(request.DirectoryFilters.TrueForAll(f => f.Kind == FilterKind.Exclude));
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
            original.Verb = SearchVerb.Apply;
            original.ReplaceTemplate = "X";
            original.FormatFlags = RegExFormatFlags.Sed;

            var copy = original.Clone();
            copy.Paths.Add("extra");

            Assert.AreEqual(1, original.Paths.Count, "original's list must not be affected by the clone");
            Assert.AreEqual(2, copy.Paths.Count);
            Assert.AreEqual(SearchVerb.Apply, copy.Verb);
            Assert.AreEqual("X", copy.ReplaceTemplate);
            Assert.AreEqual(RegExFormatFlags.Sed, copy.FormatFlags);
        }

        [TestMethod]
        public void Clone_ProducesIndependentFileNameFilters()
        {
            var original = Valid();
            original.AddIncludeFileGlobs("*.cs");

            var copy = original.Clone();
            copy.FileNameFilters.Add(new GlobFilter(FilterKind.Exclude, "*.g.cs"));

            Assert.AreEqual(1, original.FileNameFilters.Count, "original's filters must not be affected by the clone");
            Assert.AreEqual(2, copy.FileNameFilters.Count);
            Assert.AreEqual("*.cs", copy.FileNameFilters[0].Glob);
        }

        [TestMethod]
        public void Clone_ProducesIndependentDirectoryFilters()
        {
            var original = Valid();
            original.AddExcludeDirGlobs("bin");

            var copy = original.Clone();
            copy.DirectoryFilters.Add(new GlobFilter(FilterKind.Exclude, "obj"));

            Assert.AreEqual(1, original.DirectoryFilters.Count, "original's directory filters must not be affected by the clone");
            Assert.AreEqual(2, copy.DirectoryFilters.Count);
            Assert.AreEqual("bin", copy.DirectoryFilters[0].Glob);
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
