namespace UnicodeRegEx.Tools
{
    using System.Runtime.InteropServices;
    using UnicodeRegEx;

    /// <summary>
    /// Front-end-neutral code-page alias vocabulary shared by the CLI and GUI: converts between a
    /// human-friendly specifier (canonical alias or number) and the numeric code pages defined by
    /// <see cref="RegExCodePage"/>. This is tooling convenience built on top of the wrapper, kept out
    /// of the wrapper itself so the wrapper exposes only the numeric constants.
    /// </summary>
    public static class CodePages
    {
        /// <summary>
        /// Parses a code-page specifier — a canonical alias (utf8, utf-16le, latin1, acp, ...) or a
        /// non-negative number — into a code page. Aliases are case-insensitive; "acp"/"ansi" map to
        /// <see cref="RegExCodePage.SystemDefault"/> (resolve it before use). Returns false otherwise.
        /// </summary>
        public static bool TryParse(string spec, out int codePage)
        {
            var normalized = (spec ?? string.Empty).Trim();
            switch (normalized.ToLowerInvariant())
            {
                case "acp":
                case "ansi":
                    codePage = RegExCodePage.SystemDefault;
                    return true;
                case "utf8":
                case "utf-8":
                    codePage = RegExCodePage.Utf8;
                    return true;
                case "utf16":
                case "utf-16":
                case "utf16le":
                case "utf-16le":
                    codePage = RegExCodePage.Utf16LE;
                    return true;
                case "utf16be":
                case "utf-16be":
                    codePage = RegExCodePage.Utf16BE;
                    return true;
                case "latin1":
                case "iso-8859-1":
                case "iso8859-1":
                    codePage = RegExCodePage.Latin1;
                    return true;
                default:
                    return int.TryParse(normalized, out codePage) && codePage >= 0;
            }
        }

        /// <summary>
        /// Returns a short, human-readable name for a code page — its canonical alias when known,
        /// otherwise the number. Useful for help text and UI display.
        /// </summary>
        public static string GetName(int codePage)
        {
            switch (codePage)
            {
                case RegExCodePage.SystemDefault: return "acp";
                case RegExCodePage.Utf8: return "utf8";
                case RegExCodePage.Utf16LE: return "utf16le";
                case RegExCodePage.Utf16BE: return "utf16be";
                case RegExCodePage.Latin1: return "latin1";
                default: return codePage.ToString();
            }
        }

        /// <summary>
        /// Resolves the CP_ACP sentinel (<see cref="RegExCodePage.SystemDefault"/>) to the real
        /// ANSI code page; any other value is returned unchanged. This is only sentinel resolution,
        /// not a support check — call <see cref="IsSupported"/> to validate the result.
        /// </summary>
        public static int ResolveDefault(int codePage) =>
            codePage == RegExCodePage.SystemDefault ? NativeMethods.GetACP() : codePage;

        /// <summary>
        /// Returns true if the engine can decode the given (already-resolved) code page. The CP_ACP
        /// sentinel is not resolved here; resolve it with <see cref="ResolveDefault"/> first.
        /// </summary>
        public static bool IsSupported(int codePage) => RegEx.CodePageIsSupported(codePage);

        private static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            public static extern int GetACP();
        }
    }
}
