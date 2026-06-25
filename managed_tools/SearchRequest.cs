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
        // Instance fields go here:

        private int defaultCodePage = RegExCodePage.Utf8;

        // Properties with implicit backing fields go here:

        /// <summary>
        /// <see cref="DefaultCodePage"/> with the CP_ACP sentinel resolved to the real ANSI code page
        /// (kept in sync by the <see cref="DefaultCodePage"/> setter). This is the concrete page the
        /// engine decodes with; <see cref="Validate"/> reports it via
        /// <see cref="SearchRequestProblem.UnsupportedCodePage"/> if the engine cannot decode it.
        /// </summary>
        public int ResolvedDefaultCodePage { get; private set; } = RegExCodePage.Utf8;

        /// <summary>Replacement template, or null for search-only (no replacement).</summary>
        public string? ReplaceTemplate { get; set; }

        /// <summary>
        /// The operation to perform. An explicit capability rather than something inferred from
        /// <see cref="ReplaceTemplate"/> presence: a GUI keeps an always-present (possibly empty)
        /// replace box, so presence cannot imply the verb. Each front-end sets this from its own idiom
        /// (the CLI from whether <c>--replace</c> was given; a GUI from a mode control).
        /// </summary>
        public SearchVerb Verb { get; set; } = SearchVerb.Search;

        /// <summary>Match without regard to case.</summary>
        public bool IgnoreCase { get; set; }

        /// <summary>
        /// Core syntax: Extended, Literal, Basic, Perl.
        /// </summary>
        public RegExSyntaxFlags SyntaxFlags { get; set; } = RegExSyntaxFlags.Perl;

        /// <summary>
        /// True to write replacements back to files in place; false to preview only. Only meaningful
        /// when <see cref="ReplaceTemplate"/> is non-null (see <see cref="Validate"/>).
        /// </summary>
        public bool Apply { get; set; }

        /// <summary>When true, directory roots are searched recursively; otherwise only their immediate files are considered.</summary>
        public bool Recurse { get; set; }

        /// <summary>
        /// Semicolon-separated filename glob list (e.g. <c>*.cs;*.txt</c>) applied to the file name of
        /// each file found by walking a directory; null or empty means "all files". Explicitly named
        /// files are not filtered by this. Compiled via <see cref="GlobToRegex"/>.
        /// </summary>
        public string? Include { get; set; }

        /// <summary>What to do with a file that detection judges to be binary (default: <see cref="BinaryFileDisposition.Skip"/>).</summary>
        public BinaryFileDisposition BinaryDisposition { get; set; } = BinaryFileDisposition.Skip;

        /// <summary>Which encoding/binary detection steps to run (default: <see cref="EncodingDetectionOptions.Default"/> — all steps).</summary>
        public EncodingDetectionOptions EncodingDetection { get; set; } = EncodingDetectionOptions.Default;

        // Pattern and Paths come last since they are positional (not read from SearchSettings).

        /// <summary>The regular expression pattern to search for.</summary>
        public string Pattern { get; set; } = string.Empty;

        /// <summary>Files and/or directories to search. Directories are walked (recursively only when <see cref="Recurse"/> is set); explicitly named files are always included, bypassing <see cref="Include"/>.</summary>
        public List<string> Paths { get; } = new List<string>();

        // All object state (i.e. fields and Properties with implicit backing fields) go above this line.
        // Derived values go below.

        /// <summary>
        /// Code page for files without a byte-order mark, as requested — may be the CP_ACP sentinel
        /// <see cref="RegExCodePage.SystemDefault"/>. Setting this resolves the sentinel once into
        /// <see cref="ResolvedDefaultCodePage"/>, which is what callers (and the engine) should use.
        /// </summary>
        public int DefaultCodePage
        {
            get => defaultCodePage;
            set
            {
                defaultCodePage = value;
                ResolvedDefaultCodePage = CodePages.ResolveDefault(value);
            }
        }

        /// <summary>
        /// Copies the named, overridable settings (case sensitivity, encoding, replacement) from a
        /// resolved <see cref="SearchSettings"/> onto this request.
        /// </summary>
        public void ApplySettings(SearchSettings settings)
        {
            DefaultCodePage = settings.Encoding.Value;
            // ResolvedDefaultCodePage is updated by the DefaultCodePage setter.
            ReplaceTemplate = settings.Replace.Value;
            // The CLI's flag grammar: presence of --replace selects the replace verb. This
            // presence-implies-verb rule is a command-line idiom, so it lives in this translation
            // step rather than in the shared model (a GUI sets Verb from a mode control instead).
            Verb = settings.Replace.Value != null ? SearchVerb.Replace : SearchVerb.Search;
            IgnoreCase = settings.IgnoreCase.Value;
            SyntaxFlags = settings.Syntax.Value;
            Apply = settings.Apply.Value;
            Recurse = settings.Recurse.Value;
            Include = settings.Include.Value;
        }

        /// <summary>
        /// Populates <see cref="Pattern"/> and <see cref="Paths"/> from parsed positional arguments:
        /// the first positional is the pattern and the rest are paths. Missing inputs are not filled
        /// in here — an empty list leaves <see cref="Pattern"/> empty and <see cref="Paths"/> empty,
        /// which <see cref="Validate"/> reports as <see cref="SearchRequestProblem.PatternRequired"/>
        /// and <see cref="SearchRequestProblem.PathRequired"/>. Defaulting (e.g. searching the current
        /// directory) is a front-end policy, not part of this shared "command line to request" mapping.
        /// </summary>
        public void ApplyPositionals(IReadOnlyList<string> positionals)
        {
            if (positionals.Count > 0)
            {
                Pattern = positionals[0];
            }

            Paths.Clear();
            for (var i = 1; i < positionals.Count; i++)
            {
                Paths.Add(positionals[i]);
            }
        }

        /// <summary>
        /// Returns an independent copy of this request, including a separate <see cref="Paths"/> list.
        /// A job snapshots its request with this so the caller can keep editing the original (for the
        /// next run) without affecting work in progress.
        /// </summary>
        public SearchRequest Clone()
        {
            var copy = new SearchRequest
            {
                defaultCodePage = defaultCodePage,
                ResolvedDefaultCodePage = ResolvedDefaultCodePage,
                ReplaceTemplate = ReplaceTemplate,
                Verb = Verb,
                IgnoreCase = IgnoreCase,
                SyntaxFlags = SyntaxFlags,
                Apply = Apply,
                Recurse = Recurse,
                Include = Include,
                BinaryDisposition = BinaryDisposition,
                EncodingDetection = EncodingDetection,
                Pattern = Pattern,
            };

            copy.Paths.AddRange(Paths);
            return copy;
        }

        /// <summary>
        /// Returns the ways in which this request is invalid (empty when valid). A separate query
        /// rather than a constructor guard, so a front-end can bind to the model while it is being
        /// edited and surface problems in its own idiom.
        /// </summary>
        public IReadOnlyList<SearchRequestProblem> Validate()
        {
            var problems = new List<SearchRequestProblem>();
            if (Pattern.Length == 0)
            {
                problems.Add(SearchRequestProblem.PatternRequired);
            }

            if (Paths.Count == 0)
            {
                problems.Add(SearchRequestProblem.PathRequired);
            }

            if (Apply && Verb != SearchVerb.Replace)
            {
                problems.Add(SearchRequestProblem.ApplyRequiresReplace);
            }

            if (!CodePages.IsSupported(ResolvedDefaultCodePage))
            {
                problems.Add(SearchRequestProblem.UnsupportedCodePage);
            }

            return problems;
        }

        /// <summary>
        /// Renders one <see cref="SearchRequestProblem"/> (from <see cref="Validate"/>) into the
        /// command line's vocabulary (flag names, terse phrasing). An instance method so it can name
        /// the offending value — e.g. the unsupported <see cref="ResolvedDefaultCodePage"/>. Kept next
        /// to <see cref="Validate"/> and the enum so a new problem and its message are added together.
        /// Producing front-end vocabulary here is acceptable because the argument names already live in
        /// this shared library (<see cref="SearchSettings"/>); a front-end that renders problems
        /// differently (e.g. a GUI) keeps the structured <see cref="SearchRequestProblem"/> codes and
        /// simply does not call this.
        /// </summary>
        // The switch is exhaustive over the *named* members with no default arm, so adding a
        // SearchRequestProblem triggers CS8509 here and forces this text to be updated. CS8524 (the
        // synthetic, out-of-range/unnamed enum value) is suppressed because it would otherwise fire on
        // every build without indicating a real gap; an out-of-range value throws at runtime.
#pragma warning disable CS8524
        public string DescribeProblemForCommandLine(SearchRequestProblem problem) =>
            problem switch
            {
                SearchRequestProblem.PatternRequired => "no pattern given",
                SearchRequestProblem.PathRequired => "no paths given",
                SearchRequestProblem.ApplyRequiresReplace => "--apply requires --replace",
                SearchRequestProblem.UnsupportedCodePage =>
                    $"unsupported encoding '{CodePages.GetName(ResolvedDefaultCodePage)}'",
            };
#pragma warning restore CS8524
    }

    /// <summary>A way in which a <see cref="SearchRequest"/> can be invalid.</summary>
    public enum SearchRequestProblem
    {
        /// <summary>No <see cref="SearchRequest.Pattern"/> was given.</summary>
        PatternRequired,

        /// <summary>No <see cref="SearchRequest.Paths"/> were given.</summary>
        PathRequired,

        /// <summary><see cref="SearchRequest.Apply"/> is set but the verb is not <see cref="SearchVerb.Replace"/>.</summary>
        ApplyRequiresReplace,

        /// <summary><see cref="SearchRequest.ResolvedDefaultCodePage"/> is not one the engine can decode.</summary>
        UnsupportedCodePage,
    }

    /// <summary>The operation a <see cref="SearchRequest"/> performs.</summary>
    public enum SearchVerb
    {
        /// <summary>Find matches and report them (no file modification).</summary>
        Search,

        /// <summary>Replace matches using <see cref="SearchRequest.ReplaceTemplate"/> (preview unless <see cref="SearchRequest.Apply"/>).</summary>
        Replace,
    }

    /// <summary>What to do with a file that detection judges to be binary (non-text) content.</summary>
    public enum BinaryFileDisposition
    {
        /// <summary>Silently skip the file (the default).</summary>
        Skip,

        /// <summary>Report the file as an error and do not process it.</summary>
        Error,

        /// <summary>Search (or replace in) the file anyway, using the detected/default code page.</summary>
        Search,
    }
}
