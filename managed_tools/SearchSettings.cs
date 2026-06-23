namespace UnicodeRegEx.Tools
{
    using System;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools.Settings;

    /// <summary>
    /// Settings shared by every front-end of the search/replace tool: each has a sensible default,
    /// can be overridden, and is enumerable via <see cref="SettingGroup"/> for help text. Add a
    /// setting by adding a field.
    /// </summary>
    public sealed class SearchSettings : SettingGroup
    {
        public readonly FlagSetting IgnoreCase =
            new FlagSetting(SettingRole.Preference, "ignore-case", 'i', "Match without regard to case.");

        public readonly ValueSetting<int> Encoding =
            new ValueSetting<int>(
                SettingRole.Preference,
                "encoding",
                null,
                "codepage",
                "Default code page for files without a byte-order mark (utf8 | acp | <number>).",
                defaultValue: RegExCodePage.Utf8,
                parse: ParseCodePage,
                describe: CodePages.GetName);

        public readonly ValueSetting<string?> Replace =
            new ValueSetting<string?>(
                SettingRole.WorkingState,
                "replace",
                null,
                "template",
                "Replace matches using this template (preview unless --apply).",
                defaultValue: null,
                parse: value => value);

        public readonly FlagSetting Apply =
            new FlagSetting(SettingRole.WorkingState, "apply", null, "Write replacements to files in place (default: preview only).");

        public readonly FlagSetting Recurse =
            new FlagSetting(SettingRole.WorkingState, "recurse", 'r', "Search directories recursively (default: top level only).");

        public readonly ValueSetting<string?> Include =
            new ValueSetting<string?>(
                SettingRole.WorkingState,
                "include",
                null,
                "glob",
                "Only search files whose name matches this glob list (e.g. *.cs;*.txt). Explicitly named files are always searched.",
                defaultValue: null,
                parse: value => value);

        private static int ParseCodePage(string spec) =>
            CodePages.TryParse(spec, out var codePage)
                ? codePage
                : throw new FormatException($"unknown encoding '{spec}'");
    }
}
