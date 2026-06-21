namespace UnicodeRegEx.Tools
{
    using System;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools.Settings;

    /// <summary>
    /// Settings shared by every front-end of the search/replace tool: each has a sensible default,
    /// can be overridden (command line today; config/env later), and is enumerable via
    /// <see cref="SettingGroup"/> for help text and, eventually, GUI binding. Front-end concerns (help
    /// rendering, control binding) live above this; this type is meant to move into the shared core
    /// unchanged. Add a setting by adding one field.
    /// </summary>
    public sealed class SearchSettings : SettingGroup
    {
        public readonly FlagSetting IgnoreCase =
            new FlagSetting(SettingRole.Preference, "ignore-case", 'i', "Match without regard to case.");

        public readonly ValueSetting<int> Encoding =
            new ValueSetting<int>(
                SettingRole.Preference,
                "encoding",
                'e',
                "codepage",
                "Default code page for files without a byte-order mark (utf8 | acp | <number>).",
                defaultValue: RegExCodePage.Utf8,
                parse: ParseCodePage,
                describe: CodePages.GetName);

        public readonly ValueSetting<string?> Replace =
            new ValueSetting<string?>(
                SettingRole.WorkingState,
                "replace",
                'r',
                "template",
                "Replace matches using this template (preview unless --apply).",
                defaultValue: null,
                parse: value => value);

        public readonly FlagSetting Apply =
            new FlagSetting(SettingRole.WorkingState, "apply", null, "Write replacements to files in place (default: preview only).");

        private static int ParseCodePage(string spec) =>
            CodePages.TryParse(spec, out var codePage)
                ? codePage
                : throw new FormatException($"unknown encoding '{spec}'");
    }
}
