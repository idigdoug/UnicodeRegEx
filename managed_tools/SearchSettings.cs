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
        public readonly ValueSetting<string?> Replace = new ValueSetting<string?>(
            SettingRole.WorkingState,
            "replace",
            null,
            "template",
            "Replace matches using this template (preview unless --apply).",
            defaultValue: null,
            parse: value => value);

        public readonly FlagSetting Apply = new FlagSetting(
            SettingRole.WorkingState,
            "apply",
            null,
            "Write replacements to files in place (default: preview only).");

        public readonly ValueSetting<string?> Include = new ValueSetting<string?>(
            SettingRole.WorkingState,
            "include",
            null,
            "glob",
            "Only search files whose name matches this glob list (e.g. *.cs;*.txt). Explicitly named files are always searched.",
            defaultValue: null,
            parse: value => value);

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
        /// Validates cross-setting combinations that no single setting can catch, appending a message for
        /// each violation to <paramref name="errors"/> (using the command line's flag vocabulary). This is
        /// where the interaction between the replacement settings lives: <c>--apply</c> writes replacements,
        /// so it requires <c>--replace</c> to have supplied a template. <see cref="SearchRequest"/> has no
        /// equivalent invalid state to validate — its <see cref="SearchRequest.Verb"/> and non-null
        /// <see cref="SearchRequest.ReplaceTemplate"/> cannot represent "apply without a template".
        /// </summary>
        public void Validate(List<string> errors)
        {
            if (Apply.Value && Replace.Value == null)
            {
                errors.Add("--apply requires --replace");
            }
        }

        private static int ParseCodePage(string spec) =>
            CodePages.TryParse(spec, out var codePage)
                ? codePage
                : throw new FormatException($"unknown encoding '{spec}'");
    }
}
