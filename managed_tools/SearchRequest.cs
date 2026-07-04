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

        /// <summary>
        /// The full, unvalidated syntax-flags mask handed directly to the compiler (<c>RegEx.Create</c>).
        /// This is the whole mask, not just the flavor: case sensitivity and tuning bits live here too.
        /// Compose it from a flavor plus orthogonal options with <see cref="SetSyntaxFlags"/> (or the pure
        /// <see cref="ComposeSyntaxFlags"/>), or assign bits directly when you know exactly what you want.
        /// No validation happens here — the native allow-mask in <c>CreateRegEx</c> is the validity gate.
        /// </summary>
        public RegExSyntaxFlags SyntaxFlags { get; set; } = ComposeSyntaxFlags(RegExSyntaxFlags.Perl);

        /// <summary>
        /// Match-time flags applied to every search/replace in the run (default <see cref="RegExMatchFlags.Default"/>).
        /// A single raw mask handed straight to the matcher — a front-end ORs the individual flags it wants.
        /// Note: "." matching newline and multiline "^"/"$" are handled on the syntax axis
        /// (<see cref="SyntaxFlags"/>), not here, so those behaviors stay a single knob.
        /// </summary>
        public RegExMatchFlags MatchFlags { get; set; } = RegExMatchFlags.Default;

        /// <summary>
        /// True to write replacements back to files in place; false to preview only. Only meaningful
        /// when <see cref="ReplaceTemplate"/> is non-null (see <see cref="Validate"/>).
        /// </summary>
        public bool Apply { get; set; }

        /// <summary>
        /// What to do with a directory encountered during the search — applied uniformly to both
        /// directories named on input and directories discovered while recursing, after the
        /// <see cref="DirectoryFilters"/> verdict (a directory the filters exclude is pruned silently,
        /// regardless of this). Defaults to <see cref="DirectoryDisposition.Error"/>.
        /// </summary>
        public DirectoryDisposition Directories { get; set; } = DirectoryDisposition.Error;

        /// <summary>
        /// Ordered list of filename include/exclude filters applied to the file name of each file found
        /// by walking a directory (explicitly named files bypass them). Evaluated with grep's rule: the
        /// last matching filter wins; if none match, a file is included unless the first filter is an
        /// include. Empty means "all files". Use <see cref="AddIncludeFileGlobs"/> to append a semicolon glob
        /// list as include filters. Compiled and evaluated by <see cref="GlobFilterSet"/>.
        /// </summary>
        public List<GlobFilter> FileNameFilters { get; } = new List<GlobFilter>();

        /// <summary>
        /// Ordered list of include/exclude filters applied to every directory name encountered — both
        /// directories named on input and subdirectories discovered while recursing — to decide whether
        /// it is considered at all. Last matching filter wins; unlike <see cref="FileNameFilters"/>, a
        /// directory that matches no filter is <b>always included</b> (never defaults to excluded, so a
        /// leading include cannot silently prune everything). A directory the filters exclude is skipped
        /// silently, before <see cref="Directories"/> is consulted. Use <see cref="AddExcludeDirGlobs"/>
        /// for the common exclude case. Compiled and evaluated by <see cref="GlobFilterSet"/>.
        /// </summary>
        public List<GlobFilter> DirectoryFilters { get; } = new List<GlobFilter>();

        /// <summary>
        /// When true (the default), files that detection judges to be binary are skipped. Set false to
        /// search them anyway (their <see cref="SearchFile.LooksBinary"/> is still reported, so a caller
        /// that wants different handling — e.g. treat binary as an error — can do so from
        /// <see cref="ISearchSink.OnFile"/>).
        /// </summary>
        public bool SkipBinaryFiles { get; set; } = true;

        /// <summary>Which encoding/binary detection steps to run (default: <see cref="EncodingDetectionOptions.Default"/> — all steps).</summary>
        public EncodingDetectionOptions EncodingDetection { get; set; } = EncodingDetectionOptions.Default;

        // Pattern and Paths come last since they are positional (not read from SearchSettings).

        /// <summary>The regular expression pattern to search for.</summary>
        public string Pattern { get; set; } = string.Empty;

        /// <summary>Files and/or directories to search. Directories are handled per <see cref="Directories"/> and <see cref="DirectoryFilters"/>; explicitly named files are always included, bypassing <see cref="FileNameFilters"/>.</summary>
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
            SetSyntaxFlags(settings.Syntax.Value, ignoreCase: settings.IgnoreCase.Value);
            Apply = settings.Apply.Value;
            // grep semantics: -r recurses without following symlinks; without it, a directory argument is
            // reported ("Is a directory") rather than searched.
            Directories = settings.Recurse.Value ? DirectoryDisposition.RecurseNoLinks : DirectoryDisposition.Error;
            FileNameFilters.Clear();
            AddIncludeFileGlobs(settings.Include.Value);
        }

        /// <summary>
        /// Appends an include filter for each glob in a semicolon-separated list (e.g. <c>*.cs;*.txt</c>),
        /// ignoring null/empty entries. A convenience for front-ends that express includes as a single
        /// string (such as the CLI's <c>--include</c>); it preserves order and leaves any existing
        /// filters in place.
        /// </summary>
        public void AddIncludeFileGlobs(string? semicolonGlobList)
        {
            AddGlobs(FileNameFilters, FilterKind.Include, semicolonGlobList);
        }

        /// <summary>
        /// Appends an exclude <see cref="DirectoryFilters">directory filter</see> for each glob in a
        /// semicolon-separated list, ignoring null/empty entries. The directory analogue of
        /// <see cref="AddIncludeFileGlobs"/> for the common "prune these subdirectories" case (grep's
        /// <c>--exclude-dir</c>); a caller wanting a directory <em>include</em> adds a
        /// <see cref="GlobFilter"/> to <see cref="DirectoryFilters"/> directly.
        /// </summary>
        public void AddExcludeDirGlobs(string? semicolonGlobList)
        {
            AddGlobs(DirectoryFilters, FilterKind.Exclude, semicolonGlobList);
        }

        private static void AddGlobs(List<GlobFilter> target, FilterKind kind, string? semicolonGlobList)
        {
            if (semicolonGlobList == null)
            {
                return;
            }

            foreach (var glob in semicolonGlobList.Split(';'))
            {
                var trimmed = glob.Trim();
                if (trimmed.Length != 0)
                {
                    target.Add(new GlobFilter(kind, trimmed));
                }
            }
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
                SyntaxFlags = SyntaxFlags,
                MatchFlags = MatchFlags,
                Apply = Apply,
                Directories = Directories,
                SkipBinaryFiles = SkipBinaryFiles,
                EncodingDetection = EncodingDetection,
                Pattern = Pattern,
            };

            copy.Paths.AddRange(Paths);
            copy.FileNameFilters.AddRange(FileNameFilters);
            copy.DirectoryFilters.AddRange(DirectoryFilters);
            return copy;
        }

        /// <summary>
        /// Composes a full <see cref="RegExSyntaxFlags"/> mask from a syntax <paramref name="flavor"/>
        /// plus orthogonal options. Collation is treated as an independent axis: the flavor's bundled
        /// collate bit is cleared and <paramref name="collate"/> is authoritative. The Perl modifiers
        /// (<paramref name="dotAll"/>, <paramref name="freeSpacing"/>, <paramref name="multilineAnchors"/>)
        /// are applied only for perl-group flavors, because their bits alias to unrelated options in the
        /// basic syntax group. Pure and side-effect free, so a front-end can preview the resulting mask.
        /// </summary>
        public static RegExSyntaxFlags ComposeSyntaxFlags(
            RegExSyntaxFlags flavor,
            bool ignoreCase = false,
            bool collate = false,
            bool dotAll = false,
            bool freeSpacing = false,
            bool multilineAnchors = true)
        {
            // Collation is an independent axis: strip the flavor's bundled bit and let collate own it.
            var flags = flavor & ~RegExSyntaxFlags.Collate;
            if (collate)
            {
                flags |= RegExSyntaxFlags.Collate;
            }

            if (ignoreCase)
            {
                flags |= RegExSyntaxFlags.ICase;
            }

            // Bits 10-13 only carry their Perl meaning in the perl syntax group (perl, extended, awk,
            // egrep); in the basic group they alias to bk_plus_qm / bk_vbar / emacs_ex, so gate on the group.
            if ((flavor & RegExSyntaxFlags.SyntaxGroupMask) == RegExSyntaxFlags.PerlSyntaxGroup)
            {
                // DotAll is authoritative: Boost's baseline (driven by the match flags) has "." matching
                // newline, so we emit no_mod_s when off to force the conventional grep/Perl default where
                // "." does not match newline.
                flags |= dotAll ? RegExSyntaxFlags.ModS : RegExSyntaxFlags.NoModS;

                if (freeSpacing)
                {
                    flags |= RegExSyntaxFlags.ModX;
                }

                if (!multilineAnchors)
                {
                    flags |= RegExSyntaxFlags.NoModM;
                }
            }

            return flags;
        }

        /// <summary>
        /// Sets <see cref="SyntaxFlags"/> from a syntax <paramref name="flavor"/> plus orthogonal options.
        /// Instance sugar over <see cref="ComposeSyntaxFlags"/>; a caller that wants a specific bit pattern
        /// can assign <see cref="SyntaxFlags"/> directly instead.
        /// </summary>
        public void SetSyntaxFlags(
            RegExSyntaxFlags flavor = RegExSyntaxFlags.Perl,
            bool ignoreCase = false,
            bool collate = false,
            bool dotAll = false,
            bool freeSpacing = false,
            bool multilineAnchors = true)
        {
            SyntaxFlags = ComposeSyntaxFlags(flavor, ignoreCase, collate, dotAll, freeSpacing, multilineAnchors);
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

    /// <summary>What the search does with a directory it encounters (a search target or a discovered subdirectory).</summary>
    public enum DirectoryDisposition
    {
        /// <summary>Report the directory as an error (an <see cref="System.IO.IOException"/> "Is a directory") and do not search it. The default, matching grep's <c>-d read</c>.</summary>
        Error,

        /// <summary>Silently ignore the directory.</summary>
        Skip,

        /// <summary>Search the directory's immediate files but do not descend into subdirectories.</summary>
        ReadImmediateFiles,

        /// <summary>Recurse into the directory, but do not descend into subdirectories that are reparse points (symbolic links / junctions).</summary>
        RecurseNoLinks,

        /// <summary>Recurse into the directory, descending into reparse-point (symlink / junction) subdirectories too. (Cycle prevention is not yet implemented.)</summary>
        RecurseWithLinks,
    }

    /// <summary>Whether a <see cref="GlobFilter"/> includes or excludes matching names.</summary>
    public enum FilterKind
    {
        /// <summary>Names matching the glob are eligible to be searched.</summary>
        Include,

        /// <summary>Names matching the glob are not searched.</summary>
        Exclude,
    }

    /// <summary>
    /// A single ordered glob filter (used for both file names and directory names): a glob and whether a
    /// match includes or excludes the name.
    /// </summary>
    public readonly struct GlobFilter
    {
        public GlobFilter(FilterKind kind, string glob)
        {
            Kind = kind;
            Glob = glob;
        }

        /// <summary>Whether a name matching <see cref="Glob"/> is included or excluded.</summary>
        public FilterKind Kind { get; }

        /// <summary>The glob (<c>*</c> and <c>?</c> wildcards), matched against a bare file or directory name.</summary>
        public string Glob { get; }
    }
}
