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
        public void Validate_InvalidPattern_ReportsPatternInvalid()
        {
            var request = Valid();
            request.Pattern = "("; // unbalanced group

            var problems = new List<SearchRequestProblem>(request.Validate());
            CollectionAssert.Contains(problems, SearchRequestProblem.PatternInvalid);
            // An unparseable pattern is not the same as an absent one.
            CollectionAssert.DoesNotContain(problems, SearchRequestProblem.PatternRequired);
        }

        [TestMethod]
        public void Validate_InvalidPattern_DescribesNativeError()
        {
            var request = Valid();
            request.Pattern = "(";
            request.Validate();

            // The command-line description surfaces the engine's error text after the "invalid pattern:" prefix.
            var message = request.DescribeProblemForCommandLine(SearchRequestProblem.PatternInvalid);
            StringAssert.StartsWith(message, "invalid pattern");
        }

        [TestMethod]
        public void Validate_ValidPattern_DoesNotReportPatternInvalid()
        {
            var request = Valid();
            request.Pattern = "a(b|c)*d";
            CollectionAssert.DoesNotContain(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.PatternInvalid);
        }

        [TestMethod]
        public void Validate_UnsupportedMatchFlags_ReportsInvalidMatchFlags()
        {
            var request = Valid();
            // A bit outside the exposed RegExMatchFlags set.
            request.MatchFlags = (RegExMatchFlags)(1 << 30);
            CollectionAssert.Contains(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.InvalidMatchFlags);
        }

        [TestMethod]
        public void Validate_UnsupportedFormatFlags_ReportsInvalidFormatFlags()
        {
            var request = Valid();
            // Bit 0 is not an exposed format flag (boost's format_literal and other bits are rejected).
            request.FormatFlags = (RegExFormatFlags)0x1;
            CollectionAssert.Contains(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.InvalidFormatFlags);
        }

        [TestMethod]
        public void Validate_NegativeParallelism_ReportsInvalidParallelism()
        {
            var request = Valid();
            request.MaxDegreeOfParallelism = -1;
            CollectionAssert.Contains(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.InvalidParallelism);
        }

        [TestMethod]
        public void Validate_AutoParallelism_IsAllowed()
        {
            var request = Valid();
            request.MaxDegreeOfParallelism = 0; // 0 == automatic
            CollectionAssert.DoesNotContain(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.InvalidParallelism);
        }

        [TestMethod]
        public void Validate_UndefinedVerb_ReportsInvalidVerb()
        {
            var request = Valid();
            request.Verb = (SearchVerb)99;
            CollectionAssert.Contains(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.InvalidVerb);
        }

        [TestMethod]
        public void Validate_UndefinedDirectoryDisposition_ReportsInvalidDirectoryDisposition()
        {
            var request = Valid();
            request.Directories = (DirectoryDisposition)99;
            CollectionAssert.Contains(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.InvalidDirectoryDisposition);
        }

        [TestMethod]
        public void Validate_UndefinedEncodingDetectionStep_ReportsInvalidEncodingDetection()
        {
            var request = Valid();
            // A bit outside EncodingDetectionSteps.All.
            request.EncodingDetection = new EncodingDetectionOptions((EncodingDetectionSteps)(1 << 20));
            CollectionAssert.Contains(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.InvalidEncodingDetection);
        }

        [TestMethod]
        public void Validate_NoneEncodingDetection_IsAllowed()
        {
            var request = Valid();
            request.EncodingDetection = new EncodingDetectionOptions(EncodingDetectionSteps.None);
            CollectionAssert.DoesNotContain(new List<SearchRequestProblem>(request.Validate()), SearchRequestProblem.InvalidEncodingDetection);
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
        public void MakeRequest_Apply_SelectsApplyVerb()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--apply", "--replace", "X", "p", "x" }, settings, errors);
            CollectionAssert.AreEqual(new List<string>(), errors);

            var request = settings.MakeRequest();

            Assert.AreEqual(SearchVerb.Apply, request.Verb);
            Assert.AreEqual("X", request.ReplaceTemplate);
        }

        [TestMethod]
        public void MakeRequest_ReplaceWithoutApply_SelectsMatchVerb()
        {
            // --replace without --apply is a preview: the Match verb with the template as data.
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--replace", "X", "p", "x" }, settings, errors);
            CollectionAssert.AreEqual(new List<string>(), errors);

            var request = settings.MakeRequest();

            Assert.AreEqual(SearchVerb.Match, request.Verb);
            Assert.AreEqual("X", request.ReplaceTemplate);
        }

        [TestMethod]
        public void MakeRequest_NoReplace_SelectsMatchVerb_WithEmptyTemplate()
        {
            var settings = new SearchSettings();
            var request = settings.MakeRequest();

            Assert.AreEqual(SearchVerb.Match, request.Verb);
            Assert.AreEqual(string.Empty, request.ReplaceTemplate);
        }

        [TestMethod]
        public void MakeRequest_CopiesSyntaxFlavor()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "-F", "p", "x" }, settings, errors);

            var request = settings.MakeRequest();

            Assert.AreEqual(RegExSyntaxFlags.Literal, request.SyntaxFlags);
        }

        [TestMethod]
        public void MakeRequest_IgnoreCase_FoldsIntoSyntaxFlags()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "-i", "p", "x" }, settings, errors);

            var request = settings.MakeRequest();

            Assert.IsTrue(request.SyntaxFlags.HasFlag(RegExSyntaxFlags.ICase));
        }

        private static SearchRequest MakeRequestFrom(params string[] args)
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(args, settings, errors);
            CollectionAssert.AreEqual(new List<string>(), errors);
            return settings.MakeRequest();
        }

        [TestMethod]
        public void MakeRequest_SyntaxModifiers_FoldIntoSyntaxFlags()
        {
            var request = MakeRequestFrom("--mod-s", "--mod-x", "--no-mod-m", "--collate");

            Assert.IsTrue(request.SyntaxFlags.HasFlag(RegExSyntaxFlags.ModS), "mod-s -> ModS");
            Assert.IsTrue(request.SyntaxFlags.HasFlag(RegExSyntaxFlags.ModX), "mod-x -> ModX");
            Assert.IsTrue(request.SyntaxFlags.HasFlag(RegExSyntaxFlags.NoModM), "no-mod-m -> NoModM");
            Assert.IsTrue(request.SyntaxFlags.HasFlag(RegExSyntaxFlags.Collate), "collate -> Collate");
        }

        [TestMethod]
        public void MakeRequest_DefaultSyntax_HasMultilineAnchorsOn()
        {
            // Default (no --no-mod-m): multiline anchors on, so NoModM must NOT be set.
            var request = MakeRequestFrom("p", "x");
            Assert.IsFalse(request.SyntaxFlags.HasFlag(RegExSyntaxFlags.NoModM));
        }

        [TestMethod]
        public void MakeRequest_MatchFlags_ComposeIntoMatchFlags()
        {
            var request = MakeRequestFrom("--not-bol", "--not-eol", "--match-any", "--not-null", "--continuous");

            Assert.IsTrue(request.MatchFlags.HasFlag(RegExMatchFlags.NotBol));
            Assert.IsTrue(request.MatchFlags.HasFlag(RegExMatchFlags.NotEol));
            Assert.IsTrue(request.MatchFlags.HasFlag(RegExMatchFlags.Any));
            Assert.IsTrue(request.MatchFlags.HasFlag(RegExMatchFlags.NotNull));
            Assert.IsTrue(request.MatchFlags.HasFlag(RegExMatchFlags.Continuous));
        }

        [TestMethod]
        public void MakeRequest_NoMatchFlags_IsDefault()
        {
            Assert.AreEqual(RegExMatchFlags.Default, MakeRequestFrom("p", "x").MatchFlags);
        }

        [TestMethod]
        public void MakeRequest_FormatFlags_ComposeIntoFormatFlags()
        {
            var request = MakeRequestFrom("--sed", "--boost-extensions", "--no-copy", "--first-only");

            Assert.IsTrue(request.FormatFlags.HasFlag(RegExFormatFlags.Sed));
            Assert.IsTrue(request.FormatFlags.HasFlag(RegExFormatFlags.BoostExtensions));
            Assert.IsTrue(request.FormatFlags.HasFlag(RegExFormatFlags.NoCopy));
            Assert.IsTrue(request.FormatFlags.HasFlag(RegExFormatFlags.FirstOnly));
        }

        [TestMethod]
        public void MakeRequest_Directories_Choice_SelectsDisposition()
        {
            Assert.AreEqual(DirectoryDisposition.Error, MakeRequestFrom("p", "x").Directories);
            Assert.AreEqual(DirectoryDisposition.RecurseNoLinks, MakeRequestFrom("-r", "p", "x").Directories);
            Assert.AreEqual(DirectoryDisposition.RecurseWithLinks, MakeRequestFrom("-R", "p", "x").Directories);
            Assert.AreEqual(DirectoryDisposition.Skip, MakeRequestFrom("--directories-skip", "p", "x").Directories);
            Assert.AreEqual(DirectoryDisposition.ReadImmediateFiles, MakeRequestFrom("--directories-norecurse", "p", "x").Directories);
        }

        [TestMethod]
        public void MakeRequest_BinaryFiles_MapsToSkipBinaryFiles()
        {
            Assert.IsTrue(MakeRequestFrom("p", "x").SkipBinaryFiles, "default (binary) skips");
            Assert.IsTrue(MakeRequestFrom("--binary-files-binary", "p", "x").SkipBinaryFiles);
            Assert.IsTrue(MakeRequestFrom("--binary-files-without-match", "p", "x").SkipBinaryFiles);
            Assert.IsFalse(MakeRequestFrom("--binary-files-text", "p", "x").SkipBinaryFiles, "text searches");
        }

        [TestMethod]
        public void MakeRequest_EncodingDetection_DisableFlags_ClearSteps()
        {
            // Default: all steps on.
            Assert.AreEqual(EncodingDetectionSteps.All, MakeRequestFrom("p", "x").EncodingDetection.Steps);

            var request = MakeRequestFrom("--no-bom", "--no-utf8-detect", "p", "x");
            Assert.IsFalse(request.EncodingDetection.Steps.HasFlag(EncodingDetectionSteps.Bom));
            Assert.IsFalse(request.EncodingDetection.Steps.HasFlag(EncodingDetectionSteps.Utf8Heuristic));
            // Others remain on.
            Assert.IsTrue(request.EncodingDetection.Steps.HasFlag(EncodingDetectionSteps.Utf16Heuristic));
            Assert.IsTrue(request.EncodingDetection.Steps.HasFlag(EncodingDetectionSteps.BinaryNul));
            Assert.IsTrue(request.EncodingDetection.Steps.HasFlag(EncodingDetectionSteps.BinaryControlRatio));
        }

        [TestMethod]
        public void MakeRequest_Parallelism_MapsToMaxDegreeOfParallelism()
        {
            Assert.AreEqual(1, MakeRequestFrom("p", "x").MaxDegreeOfParallelism);
            Assert.AreEqual(4, MakeRequestFrom("--parallelism", "4", "p", "x").MaxDegreeOfParallelism);
            Assert.AreEqual(0, MakeRequestFrom("--parallelism", "0", "p", "x").MaxDegreeOfParallelism);
        }

        [TestMethod]
        public void Parallelism_Negative_ReportsParseError()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--parallelism", "-1", "p", "x" }, settings, errors);
            Assert.AreEqual(1, errors.Count);
        }

        [TestMethod]
        public void MakeRequest_Locale_MapsToLcid()
        {
            Assert.AreEqual(0, MakeRequestFrom("p", "x").Lcid, "default is neutral (0)");
            Assert.AreEqual(0, MakeRequestFrom("--locale", "neutral", "p", "x").Lcid);
            Assert.AreEqual(0x7F, MakeRequestFrom("--locale", "invariant", "p", "x").Lcid);
            Assert.AreEqual(1033, MakeRequestFrom("--locale", "1033", "p", "x").Lcid);
        }

        [TestMethod]
        public void Locale_DefaultText_IsNeutral()
        {
            Assert.AreEqual("neutral", new SearchSettings().Locale.DefaultText);
        }

        [TestMethod]
        public void Locale_BadValue_ReportsParseError()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--locale", "nonsense", "p", "x" }, settings, errors);
            Assert.AreEqual(1, errors.Count);
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
        public void MakeRequest_IncludeString_BecomesIncludeFilters()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--include", "*.cs;*.h", "p", "x" }, settings, errors);

            var request = settings.MakeRequest();

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
        public void MakeRequest_IncludeAndExclude_InterleaveInEncounterOrder()
        {
            // --include and --exclude feed one ordered file-name filter list; order (which drives
            // last-match-wins) must follow the command line, and each entry keeps its own kind.
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--include", "*.cs", "--exclude", "*.g.cs", "--include", "*.txt", "p", "x" }, settings, errors);
            CollectionAssert.AreEqual(new List<string>(), errors);

            var request = settings.MakeRequest();

            Assert.AreEqual(3, request.FileNameFilters.Count);
            CollectionAssert.AreEqual(
                new[] { "*.cs", "*.g.cs", "*.txt" },
                request.FileNameFilters.ConvertAll(f => f.Glob));
            CollectionAssert.AreEqual(
                new[] { FilterKind.Include, FilterKind.Exclude, FilterKind.Include },
                request.FileNameFilters.ConvertAll(f => f.Kind));
        }

        [TestMethod]
        public void MakeRequest_ExcludeDir_BecomesExcludeDirectoryFilters()
        {
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--exclude-dir", "bin", "--exclude-dir", "obj", "p", "x" }, settings, errors);
            CollectionAssert.AreEqual(new List<string>(), errors);

            var request = settings.MakeRequest();

            Assert.AreEqual(0, request.FileNameFilters.Count);
            CollectionAssert.AreEqual(
                new[] { "bin", "obj" },
                request.DirectoryFilters.ConvertAll(f => f.Glob));
            Assert.IsTrue(request.DirectoryFilters.TrueForAll(f => f.Kind == FilterKind.Exclude));
        }

        [TestMethod]
        public void GlobListSetting_Apply_Accumulates()
        {
            // A repeated option accumulates rather than replacing.
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--include", "*.cs", "--include", "*.h", "p", "x" }, settings, errors);

            CollectionAssert.AreEqual(
                new[] { "*.cs", "*.h" },
                new List<string>(new List<GlobFilter>(settings.FileNameFilters.Filters).ConvertAll(f => f.Glob)));
        }

        [TestMethod]
        public void MissingValue_ForAlias_NamesTheAliasTyped()
        {
            // --exclude is one of several bindings on the file-name-filters setting; a missing value must
            // name the alias the user typed, not the setting's canonical long name.
            var settings = new SearchSettings();
            var errors = new List<string>();
            CommandLine.Parse(new[] { "p", "x", "--exclude" }, settings, errors);

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "--exclude");
            Assert.IsFalse(errors[0].Contains("file-name-filters"));
        }

        [TestMethod]
        public void SearchSettings_Validate_ValidSettings_HasNoProblems()
        {
            var settings = new SearchSettings { Pattern = "x" };
            settings.Paths.Add(".");
            Assert.AreEqual(0, settings.Validate().Count);
        }

        [TestMethod]
        public void SearchSettings_Validate_EmptyPattern_TargetsPattern()
        {
            var settings = new SearchSettings();
            settings.Paths.Add(".");

            var problems = settings.Validate();
            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(SearchRequestProblem.PatternRequired, problems[0].Problem);
            Assert.AreEqual(SettingProblemTarget.Pattern, problems[0].Target);
            Assert.IsNull(problems[0].Setting);
        }

        [TestMethod]
        public void SearchSettings_Validate_InvalidPattern_TargetsPattern()
        {
            var settings = new SearchSettings { Pattern = "(" };
            settings.Paths.Add(".");

            var problems = settings.Validate();
            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(SearchRequestProblem.PatternInvalid, problems[0].Problem);
            Assert.AreEqual(SettingProblemTarget.Pattern, problems[0].Target);
        }

        [TestMethod]
        public void SearchSettings_Validate_NoPaths_TargetsPaths()
        {
            var settings = new SearchSettings { Pattern = "x" };

            var problems = settings.Validate();
            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(SearchRequestProblem.PathRequired, problems[0].Problem);
            Assert.AreEqual(SettingProblemTarget.Paths, problems[0].Target);
        }

        [TestMethod]
        public void SearchSettings_Validate_BadEncoding_TargetsEncodingSetting()
        {
            var settings = new SearchSettings { Pattern = "x" };
            settings.Paths.Add(".");
            var errors = new List<string>();
            CommandLine.Parse(new[] { "--encoding", "99999999" }, settings, errors);
            // The encoding parses to an unsupported code page (not a parse error), surfaced by Validate.
            CollectionAssert.AreEqual(new List<string>(), errors);

            var problems = settings.Validate();
            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(SearchRequestProblem.UnsupportedCodePage, problems[0].Problem);
            Assert.AreEqual(SettingProblemTarget.Setting, problems[0].Target);
            Assert.AreSame(settings.Encoding, problems[0].Setting);
        }

        [TestMethod]
        public void MakeRequest_CopiesPatternAndPaths()
        {
            var settings = new SearchSettings { Pattern = "abc" };
            settings.Paths.Add("a.txt");
            settings.Paths.Add("b.txt");

            var request = settings.MakeRequest();

            Assert.AreEqual("abc", request.Pattern);
            CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, request.Paths);
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
