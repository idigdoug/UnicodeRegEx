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

        public readonly ValueSetting<string> Replace = new ValueSetting<string>(
            SettingRole.WorkingState,
            "replace",
            null,
            "template",
            "Replace matches using this template (preview unless --apply).",
            defaultValue: "",
            parse: value => value);

        public readonly FlagSetting Apply = new FlagSetting(
            SettingRole.WorkingState,
            "apply",
            null,
            "Write replacements to files in place (default: preview only).");

        public readonly GlobListSetting FileNameFilters = new GlobListSetting(
            SettingRole.WorkingState,
            "include",
            null,
            "glob",
            "Only search files whose name matches this glob (repeatable; e.g. --include *.cs). Use --exclude to skip names. Explicitly named files are always searched.",
            primaryKind: FilterKind.Include,
            bindings: new[]
            {
                new CommandLineBinding("include", null, null, FilterKind.Include),
                new CommandLineBinding("exclude", null, null, FilterKind.Exclude),
            });

        public readonly GlobListSetting DirectoryFilters = new GlobListSetting(
            SettingRole.WorkingState,
            "exclude-dir",
            null,
            "glob",
            "Do not recurse into directories whose name matches this glob (repeatable; e.g. --exclude-dir bin).",
            primaryKind: FilterKind.Exclude);

        public readonly FlagSetting IgnoreCase = new FlagSetting(
            SettingRole.Preference,
            "ignore-case",
            'i',
            "Match without regard to case.");

        public readonly ValueSetting<int> Encoding = new ValueSetting<int>(
            SettingRole.Preference,
            "encoding",
            null,
            "codepage",
            "Default code page for files without a byte-order mark (utf8 | acp | <number>).",
            defaultValue: RegExCodePage.Utf8,
            parse: ParseCodePage,
            describe: CodePages.GetName);

        public readonly FlagSetting Recurse = new FlagSetting(
            SettingRole.Preference,
            "recurse",
            'r',
            "Search directories recursively (default: report a directory argument as an error).");

        public readonly ChoiceSetting<RegExSyntaxFlags> Syntax = new ChoiceSetting<RegExSyntaxFlags>(
            SettingRole.Preference,
            "syntax",
            "Regular-expression syntax flavor.",
            defaultValue: RegExSyntaxFlags.Perl,
            choices: new[]
            {
                new Choice<RegExSyntaxFlags>(RegExSyntaxFlags.Extended, "extended", 'E', "extended-regexp", "POSIX extended regular expressions (ERE)."),
                new Choice<RegExSyntaxFlags>(RegExSyntaxFlags.Literal, "fixed", 'F', "fixed-strings", "Fixed strings: the pattern is matched literally."),
                new Choice<RegExSyntaxFlags>(RegExSyntaxFlags.Basic, "basic", 'G', "basic-regexp", "POSIX basic regular expressions (BRE)."),
                new Choice<RegExSyntaxFlags>(RegExSyntaxFlags.Perl, "perl", 'P', "perl-regexp", "Perl/ECMAScript-compatible regular expressions."),
            });

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
                Directories = Recurse.Value ? DirectoryDisposition.RecurseNoLinks : DirectoryDisposition.Error,
                Pattern = Pattern,
            };

            request.SetSyntaxFlags(Syntax.Value, ignoreCase: IgnoreCase.Value);

            // The filter settings already carry ordered GlobFilter lists (include/exclude interleaved in
            // encounter order); copy them across verbatim.
            request.FileNameFilters.AddRange(FileNameFilters.Filters);
            request.DirectoryFilters.AddRange(DirectoryFilters.Filters);
            request.Paths.AddRange(Paths);

            return request;
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

                default:
                    // A problem no current setting value can cause (e.g. an out-of-range Verb/Directories
                    // built from a bool, or invalid flag masks). Report it without a specific control.
                    return SettingProblem.ForNone(problem, message);
            }
        }

        private static int ParseCodePage(string spec) =>
            CodePages.TryParse(spec, out var codePage)
                ? codePage
                : throw new FormatException($"unknown encoding '{spec}'");
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
