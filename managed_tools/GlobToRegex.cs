namespace UnicodeRegEx.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Compiles a traditional filename glob list into a single <see cref="Regex"/>. The list is a
    /// semicolon-separated set of patterns (e.g. <c>*.cs;*.txt</c>); a name matches if it matches any
    /// one of them. Within a pattern, <c>*</c> matches any run of characters and <c>?</c> matches a
    /// single character; every other character is literal. Matching is case-insensitive and uses the
    /// .NET regex engine (these are short filename checks, not the byte-oriented <see cref="RegEx"/>).
    /// </summary>
    /// <remarks>
    /// Character classes (<c>[...]</c>) are not supported yet. <c>*</c> and <c>?</c> deliberately do
    /// not cross path separators, so the same compiled pattern is also correct if it is ever matched
    /// against a path rather than a bare file name.
    /// </remarks>
    public static class GlobToRegex
    {
        private static readonly char[] Semicolon = new[] { ';' };

        /// <summary>
        /// Compiles <paramref name="globList"/> (a semicolon-separated glob list) into a single regex,
        /// or returns null if the list is null/empty/only separators (meaning "match everything").
        /// </summary>
        public static Regex? Compile(string? globList)
        {
            if (globList == null)
            {
                return null;
            }

            var alternatives = new List<string>();
            foreach (var pattern in globList.Split(Semicolon, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = pattern.Trim();
                if (trimmed.Length != 0)
                {
                    alternatives.Add(TranslateGlobToAlternative(trimmed));
                }
            }

            if (alternatives.Count == 0)
            {
                return null;
            }

            var combined = "^(?:" + string.Join("|", alternatives) + ")$";
            return new Regex(combined, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Translates a single glob into a regex alternative body (no anchors), suitable for combining
        /// several globs into one alternation. <c>*</c> matches any run of characters and <c>?</c> a
        /// single character; neither crosses a path separator, and every other character is literal.
        /// </summary>
        internal static string TranslateGlobToAlternative(string glob)
        {
            var sb = new StringBuilder(glob.Length * 2);
            foreach (var c in glob)
            {
                switch (c)
                {
                    case '*':
                        sb.Append("[^\\\\/]*");
                        break;
                    case '?':
                        sb.Append("[^\\\\/]");
                        break;
                    default:
                        sb.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
