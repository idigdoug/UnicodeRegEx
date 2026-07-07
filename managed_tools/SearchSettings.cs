namespace UnicodeRegEx.Tools
{
    using System;
    using System.Collections.Generic;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools.Settings;

    /// <summary>
    /// Settings shared by every front-end of the search/replace tool: each has a sensible default,
    /// can be overridden, and is enumerable via <see cref="SettingGroup"/> for help text. Add a
    /// setting by adding a field.
    /// </summary>
    public sealed class SearchSettings : SettingGroup
    {
        /// <summary>
        /// The regular-expression pattern to search for. Plain data (not a <see cref="Settings.Setting"/>):
        /// it is a primary-UI input, so it does not appear on an auto-generated advanced property page or in
        /// generated help. Included on the model so a front-end binds the whole search to one object and
        /// <see cref="MakeRequest"/> / <see cref="Validate"/> cover it.
        /// </summary>
        public string Pattern { get; set; } = string.Empty;

        /// <summary>
        /// Files and/or directories to search. Plain data (not a <see cref="Settings.Setting"/>) for the
        /// same reasons as <see cref="Pattern"/>: a primary-UI input, off the property page and help.
        /// </summary>
        public List<string> Paths { get; } = new List<string>();

        // Pattern Syntax

        public readonly ChoiceSetting<RegExSyntaxFlags> SyntaxFlavor = new ChoiceSetting<RegExSyntaxFlags>(
            SettingRole.Preference,
            SettingCategory.PatternSyntax,
            "syntax-flavor",
            "Regular-expression syntax flavor.",
            defaultValue: RegExSyntaxFlags.Perl,
            choices: new[]
            {
                new Choice<RegExSyntaxFlags>(RegExSyntaxFlags.Perl, "perl", 'P', "perl-regexp", "Perl/ECMAScript-compatible regular expressions."),
                new Choice<RegExSyntaxFlags>(RegExSyntaxFlags.Literal, "fixed", 'F', "fixed-strings", "Fixed strings: the pattern is matched literally."),
                new Choice<RegExSyntaxFlags>(RegExSyntaxFlags.Basic, "basic", 'G', "basic-regexp", "POSIX basic regular expressions (BRE)."),
                new Choice<RegExSyntaxFlags>(RegExSyntaxFlags.Extended, "extended", 'E', "extended-regexp", "POSIX extended regular expressions (ERE)."),
            });

        // Advanced syntax modifiers (native boost flag names, so their documentation is discoverable). Each
        // maps 1:1 to a syntax-flags bit and composes into SyntaxFlags in MakeRequest.

        public readonly FlagSetting ModS = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.PatternSyntax,
            "mod-s",
            null,
            "Advanced: enable the Perl 's' modifier (mod_s) so '.' matches newline.");

        public readonly FlagSetting ModX = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.PatternSyntax,
            "mod-x",
            null,
            "Advanced: enable the Perl 'x' modifier (mod_x) so unescaped whitespace in the pattern is ignored (free-spacing).");

        public readonly FlagSetting NoModM = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.PatternSyntax,
            "no-mod-m",
            null,
            "Advanced: disable the Perl 'm' modifier (no_mod_m) so '^' and '$' match only at the start/end of input, not at embedded newlines.");

        public readonly FlagSetting Collate = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.PatternSyntax,
            "collate",
            null,
            "Advanced: use locale-specific collation (collate) in character ranges such as [a-b].");

        // Matching

        public readonly FlagSetting IgnoreCase = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Matching,
            "ignore-case",
            'i',
            "Match without regard to case.");

        public readonly ValueSetting<int> Locale = new ValueSetting<int>(
            SettingRole.Preference,
            SettingCategory.Matching,
            "locale",
            null,
            "lcid",
            "Locale used for case-folding and collation. Can be neutral, invariant, or a Windows LCID number.",
            defaultValue: 0,
            editorKind: EditorKind.Integer,
            parse: ParseLcid,
            describe: DescribeLcid);

        // Advanced match flags (native boost match_flag names). Each maps 1:1 to a match-flags bit and is
        // OR'd into MatchFlags in MakeRequest.

        public readonly FlagSetting MatchNotBol = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Matching,
            "not-bol",
            null,
            "Advanced: '^' does not match at the start of the input (match_not_bol).");

        public readonly FlagSetting MatchNotEol = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Matching,
            "not-eol",
            null,
            "Advanced: '$' does not match at the end of the input (match_not_eol).");

        public readonly FlagSetting MatchNotBob = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Matching,
            "not-bob",
            null,
            "Advanced: '\\A', '\\`' do not match at the start of the input (match_not_bob).");

        public readonly FlagSetting MatchNotEob = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Matching,
            "not-eob",
            null,
            "Advanced: '\\'', '\\z', '\\Z' do not match at the end of the input (match_not_eob).");

        public readonly FlagSetting MatchNotBow = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Matching,
            "not-bow",
            null,
            "Advanced: '\\b', '\\<' do not match at the start of the input (match_not_bow).");

        public readonly FlagSetting MatchNotEow = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Matching,
            "not-eow",
            null,
            "Advanced: '\\b', '\\>' do not match at the end of the input (match_not_eow).");

        public readonly FlagSetting MatchAny = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Matching,
            "match-any",
            null,
            "Advanced: accept any match, not necessarily the best one at a position (match_any); faster.");

        public readonly FlagSetting MatchNotNull = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Matching,
            "not-null",
            null,
            "Advanced: the expression may not match an empty sequence (match_not_null).");

        public readonly FlagSetting MatchContinuous = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Matching,
            "continuous",
            null,
            "Advanced: the match must begin at the start of the search range (match_continuous).");

        // Replacement

        public readonly ValueSetting<string> Replace = new ValueSetting<string>(
            SettingRole.WorkingState,
            SettingCategory.Replacement,
            "replace",
            null,
            "template",
            "Replace matches using this template (preview-only unless --apply).",
            defaultValue: "",
            editorKind: EditorKind.Text,
            parse: value => value);

        public readonly FlagSetting Apply = new FlagSetting(
            SettingRole.WorkingState,
            SettingCategory.Replacement,
            "apply",
            null,
            "Write replacements to files.");

        // Advanced replacement (format) flags (native boost format_* names). Each maps 1:1 to a
        // format-flags bit and is OR'd into FormatFlags in MakeRequest.

        public readonly FlagSetting Sed = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Replacement,
            "sed",
            null,
            "Advanced: interpret the replacement template with Unix sed rules (format_sed).");

        public readonly FlagSetting BoostExtensions = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Replacement,
            "boost-extensions",
            null,
            "Advanced: enable Boost replacement extensions (format_all), e.g. conditionals like (?n:true:false).");

        public readonly FlagSetting NoCopy = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Replacement,
            "no-copy",
            null,
            "Advanced: do not copy the unmatched portions of the input to the output (format_no_copy).");

        public readonly FlagSetting FirstOnly = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Replacement,
            "first-only",
            null,
            "Advanced: replace only the first match (format_first_only).");

        // File and Directory Selection

        public readonly GlobListSetting FileNameFilters = new GlobListSetting(
            SettingRole.WorkingState,
            SettingCategory.FileAndDirectorySelection,
            "file-name-filters",
            null,
            "glob",
            "Only search files whose name matches this glob (e.g. --include *.cs). Repeatable. Use --exclude to skip names. Explicitly named files are always searched.",
            primaryKind: FilterKind.Include,
            bindings: new[]
            {
                new CommandLineBinding("include", null, null, FilterKind.Include),
                new CommandLineBinding("exclude", null, null, FilterKind.Exclude),
            });

        public readonly GlobListSetting DirectoryFilters = new GlobListSetting(
            SettingRole.WorkingState,
            SettingCategory.FileAndDirectorySelection,
            "directory-filters",
            null,
            "glob",
            "Do not recurse into directories whose name matches this glob (e.g. --exclude-dir .*). Repeatable.",
            primaryKind: FilterKind.Exclude,
            bindings: new[]
            {
                new CommandLineBinding("exclude-dir", null, null, FilterKind.Exclude),
            });

        public readonly ChoiceSetting<DirectoryDisposition> Directories = new ChoiceSetting<DirectoryDisposition>(
            SettingRole.Preference,
            SettingCategory.FileAndDirectorySelection,
            "directories",
            "What to do with a directory encountered as an input or while recursing.",
            defaultValue: DirectoryDisposition.Error,
            choices: new[]
            {
                new Choice<DirectoryDisposition>(DirectoryDisposition.Error, "error", null, "directories-error", "Report a directory as an error (the default)."),
                new Choice<DirectoryDisposition>(DirectoryDisposition.Skip, "skip", null, "directories-skip", "Silently ignore directories."),
                new Choice<DirectoryDisposition>(DirectoryDisposition.ReadImmediateFiles, "norecurse", null, "directories-norecurse", "Search a directory's immediate files but do not descend."),
                new Choice<DirectoryDisposition>(DirectoryDisposition.RecurseNoLinks, "recurse", 'r', "recursive", "Recurse into directories, skipping symlink/junction subdirectories."),
                new Choice<DirectoryDisposition>(DirectoryDisposition.RecurseWithLinks, "recurse-links", 'R', "dereference-recursive", "Recurse into directories, following symlink/junction subdirectories."),
            });

        public readonly ChoiceSetting<string> BinaryFiles = new ChoiceSetting<string>(
            SettingRole.Preference,
            SettingCategory.FileAndDirectorySelection,
            "binary-files",
            "How to treat files that detection judges to be binary.",
            defaultValue: "binary",
            choices: new[]
            {
                new Choice<string>("binary", "binary", null, "binary-files-binary", "Skip files that look binary (the default)."),
                new Choice<string>("without-match", "without-match", null, "binary-files-without-match", "Skip files that look binary (same as binary for this tool)."),
                new Choice<string>("text", "text", null, "binary-files-text", "Search files that look binary as if they were text."),
            });

        // Encoding

        public readonly ValueSetting<int> Encoding = new ValueSetting<int>(
            SettingRole.Preference,
            SettingCategory.Encoding,
            "encoding",
            null,
            "codepage",
            "Text encoding to use for files where encoding was not automatically detected. Can be utf8, utf16, utf16be, latin1, or <win32-codepage-number>.",
            defaultValue: RegExCodePage.Latin1,
            editorKind: EditorKind.Integer,
            parse: ParseCodePage,
            describe: CodePages.GetName);

        // Encoding/binary detection steps are all on by default; these disable individual steps and are
        // composed into EncodingDetection in MakeRequest.

        public readonly FlagSetting NoBom = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Encoding,
            "no-bom",
            null,
            "Advanced: do not look for a byte-order mark when detecting encoding.");

        public readonly FlagSetting NoUtf16Detect = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Encoding,
            "no-utf16-detect",
            null,
            "Advanced: do not try to detect UTF-16 by NUL-parity signature.");

        public readonly FlagSetting NoUtf8Detect = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Encoding,
            "no-utf8-detect",
            null,
            "Advanced: do not try to detect UTF-8 by strict validation.");

        public readonly FlagSetting NoNulBinary = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Encoding,
            "no-nul-binary",
            null,
            "Advanced: do not try to detect binary files by NUL byte presence.");

        public readonly FlagSetting NoControlRatioBinary = new FlagSetting(
            SettingRole.Preference,
            SettingCategory.Encoding,
            "no-control-ratio-binary",
            null,
            "Advanced: do not try to detect binary files by control byte ratio.");

        // Performance

        public readonly ValueSetting<int> Parallelism = new ValueSetting<int>(
            SettingRole.Preference,
            SettingCategory.Performance,
            "parallelism",
            null,
            "count",
            "Maximum number of files processed concurrently (1 = serial, 0 = CPU count).",
            defaultValue: 1,
            editorKind: EditorKind.Integer,
            parse: ParseParallelism);

        /// <summary>
        /// Builds a fully-populated <see cref="SearchRequest"/> from this model — the single translation
        /// from front-end settings to engine input. A GUI edits this model and calls <see cref="MakeRequest"/>
        /// to run; the CLI populates the model (including <see cref="Pattern"/>/<see cref="Paths"/> from its
        /// positionals) and does the same.
        /// </summary>
        public SearchRequest MakeRequest()
        {
            var request = new SearchRequest
            {
                DefaultCodePage = Encoding.Value,
                // ResolvedDefaultCodePage is updated by the DefaultCodePage setter.
                ReplaceTemplate = Replace.Value,
                Verb = Apply.Value ? SearchVerb.Apply : SearchVerb.Match,
                Directories = Directories.Value,
                SkipBinaryFiles = BinaryFiles.Value != "text",
                EncodingDetection = ComposeEncodingDetection(),
                MaxDegreeOfParallelism = Parallelism.Value,
                Lcid = Locale.Value,
                Pattern = Pattern,
            };

            request.SetSyntaxFlags(
                SyntaxFlavor.Value,
                ignoreCase: IgnoreCase.Value,
                collate: Collate.Value,
                dotAll: ModS.Value,
                freeSpacing: ModX.Value,
                multilineAnchors: !NoModM.Value);

            request.MatchFlags = ComposeMatchFlags();
            request.FormatFlags = ComposeFormatFlags();

            // The filter settings already carry ordered GlobFilter lists (include/exclude interleaved in
            // encounter order); copy them across verbatim.
            request.FileNameFilters.AddRange(FileNameFilters.Filters);
            request.DirectoryFilters.AddRange(DirectoryFilters.Filters);
            request.Paths.AddRange(Paths);

            return request;
        }

        private RegExMatchFlags ComposeMatchFlags()
        {
            var flags = RegExMatchFlags.Default;
            if (MatchNotBol.Value) flags |= RegExMatchFlags.NotBol;
            if (MatchNotEol.Value) flags |= RegExMatchFlags.NotEol;
            if (MatchNotBob.Value) flags |= RegExMatchFlags.NotBob;
            if (MatchNotEob.Value) flags |= RegExMatchFlags.NotEob;
            if (MatchNotBow.Value) flags |= RegExMatchFlags.NotBow;
            if (MatchNotEow.Value) flags |= RegExMatchFlags.NotEow;
            if (MatchAny.Value) flags |= RegExMatchFlags.Any;
            if (MatchNotNull.Value) flags |= RegExMatchFlags.NotNull;
            if (MatchContinuous.Value) flags |= RegExMatchFlags.Continuous;
            return flags;
        }

        private RegExFormatFlags ComposeFormatFlags()
        {
            var flags = RegExFormatFlags.Perl;
            if (Sed.Value) flags |= RegExFormatFlags.Sed;
            if (BoostExtensions.Value) flags |= RegExFormatFlags.BoostExtensions;
            if (NoCopy.Value) flags |= RegExFormatFlags.NoCopy;
            if (FirstOnly.Value) flags |= RegExFormatFlags.FirstOnly;
            return flags;
        }

        private EncodingDetectionOptions ComposeEncodingDetection()
        {
            // Detection is all-on by default; each flag disables one step.
            var steps = EncodingDetectionSteps.All;
            if (NoBom.Value) steps &= ~EncodingDetectionSteps.Bom;
            if (NoUtf16Detect.Value) steps &= ~EncodingDetectionSteps.Utf16Heuristic;
            if (NoUtf8Detect.Value) steps &= ~EncodingDetectionSteps.Utf8Heuristic;
            if (NoNulBinary.Value) steps &= ~EncodingDetectionSteps.BinaryNul;
            if (NoControlRatioBinary.Value) steps &= ~EncodingDetectionSteps.BinaryControlRatio;
            return new EncodingDetectionOptions(steps);
        }

        private static int ParseParallelism(string spec)
        {
            if (int.TryParse(spec, out var value) && value >= 0)
            {
                return value;
            }

            throw new FormatException($"invalid parallelism '{spec}' (must be a non-negative integer)");
        }

        /// <summary>
        /// Validates the whole model by building its <see cref="SearchRequest"/> and running the request's
        /// validation, returning one <see cref="SettingProblem"/> per problem tagged with the control a
        /// front-end should highlight — a <see cref="Settings.Setting"/>, or the <see cref="Pattern"/> /
        /// <see cref="Paths"/> primary-UI inputs. Encapsulates the make-then-validate-then-map steps so a
        /// caller does not repeat them.
        /// </summary>
        public IReadOnlyList<SettingProblem> Validate()
        {
            var request = MakeRequest();

            var problems = new List<SettingProblem>();
            foreach (var problem in request.Validate())
            {
                problems.Add(MapProblem(problem, request));
            }

            return problems;
        }

        // Maps a request-level problem to the control a front-end should flag: a specific setting when one is
        // responsible, otherwise the pattern or paths primary-UI input. Extend as settings that can carry an
        // invalid value are added.
        private SettingProblem MapProblem(SearchRequestProblem problem, SearchRequest request)
        {
            var message = request.DescribeProblemForCommandLine(problem);
            switch (problem)
            {
                case SearchRequestProblem.PatternRequired:
                case SearchRequestProblem.PatternInvalid:
                    return SettingProblem.ForPattern(problem, message);

                case SearchRequestProblem.PathRequired:
                    return SettingProblem.ForPaths(problem, message);

                case SearchRequestProblem.UnsupportedCodePage:
                    return SettingProblem.ForSetting(problem, Encoding, message);

                case SearchRequestProblem.InvalidParallelism:
                    return SettingProblem.ForSetting(problem, Parallelism, message);

                default:
                    // A problem no current setting value can cause (e.g. an out-of-range Verb/Directories,
                    // or invalid flag masks composed from individual flags). Report it without a specific
                    // control.
                    return SettingProblem.ForNone(problem, message);
            }
        }

        private static int ParseCodePage(string spec) =>
            CodePages.TryParse(spec, out var codePage)
                ? codePage
                : throw new FormatException($"unknown encoding '{spec}'");

        // A small set of friendly locale names plus a raw non-negative LCID number.
        private const int LocaleNeutral = 0;    // LOCALE_NEUTRAL
        private const int LocaleInvariant = 0x7F; // LOCALE_INVARIANT

        private static int ParseLcid(string spec)
        {
            switch (spec.Trim().ToLowerInvariant())
            {
                case "neutral":
                    return LocaleNeutral;
                case "invariant":
                    return LocaleInvariant;
                default:
                    if (int.TryParse(spec, out var lcid) && lcid >= 0)
                    {
                        return lcid;
                    }

                    throw new FormatException($"invalid locale '{spec}' (use neutral, invariant, or a Windows LCID number)");
            }
        }

        private static string DescribeLcid(int lcid)
        {
            switch (lcid)
            {
                case LocaleNeutral: return "neutral";
                case LocaleInvariant: return "invariant";
                default: return lcid.ToString();
            }
        }
    }

    /// <summary>Which input a <see cref="SettingProblem"/> is about — the control a front-end should flag.</summary>
    public enum SettingProblemTarget
    {
        /// <summary>A specific <see cref="Settings.Setting"/> (see <see cref="SettingProblem.Setting"/>).</summary>
        Setting,

        /// <summary>The <see cref="SearchSettings.Pattern"/> primary-UI input.</summary>
        Pattern,

        /// <summary>The <see cref="SearchSettings.Paths"/> primary-UI input.</summary>
        Paths,

        /// <summary>No specific control (a problem no current input value can cause).</summary>
        None,
    }

    /// <summary>
    /// A validation problem discovered by <see cref="SearchSettings.Validate"/>, pairing the underlying
    /// <see cref="SearchRequestProblem"/> with the control a front-end should highlight (a
    /// <see cref="Settings.Setting"/>, or the pattern/paths inputs — see <see cref="Target"/>) and a
    /// human-readable message.
    /// </summary>
    public readonly struct SettingProblem
    {
        private SettingProblem(SearchRequestProblem problem, SettingProblemTarget target, Setting? setting, string message)
        {
            Problem = problem;
            Target = target;
            Setting = setting;
            Message = message;
        }

        /// <summary>The underlying request-level problem.</summary>
        public SearchRequestProblem Problem { get; }

        /// <summary>Which input the problem is about.</summary>
        public SettingProblemTarget Target { get; }

        /// <summary>
        /// The setting responsible for the problem when <see cref="Target"/> is
        /// <see cref="SettingProblemTarget.Setting"/>; otherwise null.
        /// </summary>
        public Setting? Setting { get; }

        /// <summary>A human-readable description of the problem.</summary>
        public string Message { get; }

        internal static SettingProblem ForSetting(SearchRequestProblem problem, Setting setting, string message) =>
            new SettingProblem(problem, SettingProblemTarget.Setting, setting, message);

        internal static SettingProblem ForPattern(SearchRequestProblem problem, string message) =>
            new SettingProblem(problem, SettingProblemTarget.Pattern, null, message);

        internal static SettingProblem ForPaths(SearchRequestProblem problem, string message) =>
            new SettingProblem(problem, SettingProblemTarget.Paths, null, message);

        internal static SettingProblem ForNone(SearchRequestProblem problem, string message) =>
            new SettingProblem(problem, SettingProblemTarget.None, null, message);
    }
}
