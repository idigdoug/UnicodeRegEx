namespace UnicodeRegEx.Tools
{
    using System;
    using UnicodeRegEx;
    using UnicodeRegEx.CommandLine;

    /// <summary>
    /// Settings shared by every front-end of the search/replace tool: each has a sensible default,
    /// can be overridden (command line today; config/env later), and is enumerable via
    /// <see cref="OptionSet"/> for help text and, eventually, GUI binding. Front-end concerns (help
    /// rendering, control binding) live above this; this type is meant to move into the shared core
    /// unchanged. Add a setting by adding one field.
    /// </summary>
    public sealed class SearchSettings : OptionSet
    {
        public readonly FlagOption IgnoreCase =
            new FlagOption("ignore-case", 'i', "Match without regard to case.");

        public readonly ValueOption<int> Encoding =
            new ValueOption<int>(
                "encoding",
                'e',
                "codepage",
                "Default code page for files without a byte-order mark (utf8 | acp | <number>).",
                defaultValue: RegExCodePage.Utf8,
                parse: ParseCodePage,
                describe: RegExCodePage.GetName);

        public readonly ValueOption<string?> Replace =
            new ValueOption<string?>(
                "replace",
                'r',
                "template",
                "Replace matches using this template (preview unless --apply).",
                defaultValue: null,
                parse: value => value);

        public readonly FlagOption Apply =
            new FlagOption("apply", null, "Write replacements to files in place (default: preview only).");

        private static int ParseCodePage(string spec) =>
            RegExCodePage.TryParse(spec, out var codePage)
                ? codePage
                : throw new FormatException($"unknown encoding '{spec}'");
    }
}
