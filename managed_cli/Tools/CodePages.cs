namespace UnicodeRegEx.Tools
{
    using System;
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
        /// Resolves a requested default code page to a concrete, engine-supported page: the CP_ACP
        /// sentinel (<see cref="RegExCodePage.SystemDefault"/>) becomes the real ANSI code page via
        /// <paramref name="getAnsiCodePage"/>, then an unsupported result falls back to UTF-8. The
        /// decision is shared; <see cref="CodePageResolution.UsedFallback"/> lets each front-end
        /// render the fallback in its own idiom. <paramref name="getAnsiCodePage"/> is injected so
        /// this layer stays platform-clean and testable.
        /// </summary>
        public static CodePageResolution ResolveDefault(int codePage, Func<int> getAnsiCodePage)
        {
            if (codePage == RegExCodePage.SystemDefault)
            {
                codePage = getAnsiCodePage();
            }

            return RegEx.IsCodePageSupported(codePage)
                ? new CodePageResolution(codePage, codePage, usedFallback: false)
                : new CodePageResolution(RegExCodePage.Utf8, codePage, usedFallback: true);
        }
    }

    /// <summary>The outcome of <see cref="CodePages.ResolveDefault"/>.</summary>
    public readonly struct CodePageResolution
    {
        public CodePageResolution(int codePage, int requested, bool usedFallback)
        {
            CodePage = codePage;
            Requested = requested;
            UsedFallback = usedFallback;
        }

        /// <summary>The concrete, engine-supported code page to use.</summary>
        public int CodePage { get; }

        /// <summary>The requested page (after resolving the CP_ACP sentinel), for messaging.</summary>
        public int Requested { get; }

        /// <summary>True when <see cref="Requested"/> was unsupported and UTF-8 was substituted.</summary>
        public bool UsedFallback { get; }
    }
}
