namespace UnicodeRegEx.Tools
{
    using System.Collections.Generic;
    using UnicodeRegEx;

    /// <summary>
    /// The raw, editable inputs for a single search/replace operation: what to search, how to
    /// interpret it, and what (if anything) to write back. A mutable model that front-ends populate
    /// directly — the CLI assigns from parsed settings; a GUI binds controls to it. It may hold
    /// transiently invalid combinations while being edited, so it is a dumb data holder: call
    /// <see cref="Validate"/> before use rather than guarding in a constructor. Engine-ready values
    /// (syntax flags, resolved code page) are derived at use time via <see cref="SyntaxFlags"/> and
    /// <see cref="CodePages.ResolveDefault"/>, not stored. Front-end-neutral so it can move into the
    /// shared core unchanged.
    /// </summary>
    public sealed class SearchRequest
    {
        /// <summary>The regular expression pattern to search for.</summary>
        public string Pattern { get; set; } = string.Empty;

        /// <summary>Files and/or directories to search; directories are searched recursively.</summary>
        public List<string> Paths { get; } = new List<string>();

        /// <summary>Match without regard to case.</summary>
        public bool IgnoreCase { get; set; }

        /// <summary>
        /// Code page for files without a byte-order mark, as requested — may be
        /// <see cref="RegExCodePage.SystemDefault"/>; resolve via <see cref="CodePages.ResolveDefault"/>
        /// before handing a concrete page to the engine.
        /// </summary>
        public int DefaultCodePage { get; set; } = RegExCodePage.Utf8;

        /// <summary>Replacement template, or null for search-only (no replacement).</summary>
        public string? ReplaceTemplate { get; set; }

        /// <summary>
        /// True to write replacements back to files in place; false to preview only. Only meaningful
        /// when <see cref="ReplaceTemplate"/> is non-null (see <see cref="Validate"/>).
        /// </summary>
        public bool Apply { get; set; }

        /// <summary>True when this request performs replacement (preview or in place).</summary>
        public bool IsReplace => ReplaceTemplate != null;

        /// <summary>Syntax/option flags compiled from the editable options (e.g. <see cref="IgnoreCase"/>).</summary>
        public RegExSyntaxFlags SyntaxFlags =>
            IgnoreCase
                ? RegExSyntaxFlags.ECMAScript | RegExSyntaxFlags.ICase
                : RegExSyntaxFlags.ECMAScript;

        /// <summary>
        /// Returns the ways in which this request is invalid (empty when valid). A separate query
        /// rather than a constructor guard, so a front-end can bind to the model while it is being
        /// edited and surface problems in its own idiom.
        /// </summary>
        public IReadOnlyList<SearchRequestProblem> Validate()
        {
            var problems = new List<SearchRequestProblem>();
            if (Apply && ReplaceTemplate == null)
            {
                problems.Add(SearchRequestProblem.ApplyRequiresTemplate);
            }

            return problems;
        }
    }

    /// <summary>A way in which a <see cref="SearchRequest"/> can be invalid.</summary>
    public enum SearchRequestProblem
    {
        /// <summary><see cref="SearchRequest.Apply"/> is set but no replacement template was given.</summary>
        ApplyRequiresTemplate,
    }
}
