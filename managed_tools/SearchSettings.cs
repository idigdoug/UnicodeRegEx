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
        /// Validates the settings by mapping them onto a <see cref="SearchRequest"/> and running its
        /// validation, returning one <see cref="SettingProblem"/> per problem that a setting is responsible
        /// for (so a front-end can highlight the offending control). Encapsulates the
        /// apply-then-validate-then-map steps so a caller does not repeat them.
        /// <para>
        /// Only problems attributable to a setting are returned. Problems rooted in the primary-UI inputs
        /// that are not settings — the pattern and the paths (<see cref="SearchRequest.Pattern"/> /
        /// <see cref="SearchRequest.Paths"/>) — are the front-end's own responsibility to validate against
        /// the real request and are intentionally omitted here (this method applies no pattern or paths).
        /// </para>
        /// </summary>
        public IReadOnlyList<SettingProblem> Validate()
        {
            var request = new SearchRequest();
            request.ApplySettings(this);

            var problems = new List<SettingProblem>();
            foreach (var problem in request.Validate())
            {
                var setting = SettingFor(problem);
                if (setting != null)
                {
                    problems.Add(new SettingProblem(problem, setting, request.DescribeProblemForCommandLine(problem)));
                }
            }

            return problems;
        }

        // Maps a request-level problem back to the setting that produced it, or null when no setting is
        // responsible (e.g. the pattern/paths, which are primary-UI inputs, or problems a setting value can
        // never cause such as an out-of-range Verb/Directories built from a bool). Extend as settings that
        // can carry an invalid value are added.
        private Setting? SettingFor(SearchRequestProblem problem)
        {
            switch (problem)
            {
                case SearchRequestProblem.UnsupportedCodePage:
                    return Encoding;

                default:
                    return null;
            }
        }

        private static int ParseCodePage(string spec) =>
            CodePages.TryParse(spec, out var codePage)
                ? codePage
                : throw new FormatException($"unknown encoding '{spec}'");
    }

    /// <summary>
    /// A validation problem discovered by <see cref="SearchSettings.Validate"/>, pairing the underlying
    /// <see cref="SearchRequestProblem"/> with the <see cref="Settings.Setting"/> a front-end should
    /// highlight and a human-readable message.
    /// </summary>
    public readonly struct SettingProblem
    {
        public SettingProblem(SearchRequestProblem problem, Setting setting, string message)
        {
            Problem = problem;
            Setting = setting;
            Message = message;
        }

        /// <summary>The underlying request-level problem.</summary>
        public SearchRequestProblem Problem { get; }

        /// <summary>The setting responsible for the problem (the control a front-end should flag).</summary>
        public Setting Setting { get; }

        /// <summary>A human-readable description of the problem.</summary>
        public string Message { get; }
    }
}
