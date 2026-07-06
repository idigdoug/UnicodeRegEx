namespace UnicodeRegEx.Tools
{
    using System;
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

        // The engine's error text from the most recent failed pattern compile in Validate(), used by
        // DescribeProblemForCommandLine to explain a PatternInvalid problem. Null until a compile fails.
        private string? lastPatternError;

        // Properties with implicit backing fields go here:

        /// <summary>
        /// <see cref="DefaultCodePage"/> with the CP_ACP sentinel resolved to the real ANSI code page
        /// (kept in sync by the <see cref="DefaultCodePage"/> setter). This is the concrete page the
        /// engine decodes with; <see cref="Validate"/> reports it via
        /// <see cref="SearchRequestProblem.UnsupportedCodePage"/> if the engine cannot decode it.
        /// </summary>
        public int ResolvedDefaultCodePage { get; private set; } = RegExCodePage.Utf8;

        /// <summary>
        /// The replacement template applied to each match in <see cref="SearchVerb.Apply"/> (and available
        /// as a preview in <see cref="SearchVerb.Match"/>). This is data, not a mode — it never affects
        /// whether the run matches or applies; only <see cref="Verb"/> does. Defaults to the empty string;
        /// an empty template replaces each match with nothing. (It is passed to the engine as a BSTR, which
        /// does not distinguish empty from null, so the model uses a non-null empty string throughout.)
        /// </summary>
        public string ReplaceTemplate { get; set; } = string.Empty;

        /// <summary>
        /// The operation to perform — the single, authoritative switch between the engine's two actions:
        /// <see cref="SearchVerb.Match"/> (find and report matches) and <see cref="SearchVerb.Apply"/>
        /// (write replacements to files). Set explicitly by each front-end from its own idiom (the CLI from
        /// whether <c>--apply</c> was given; a GUI from a mode control); it is never inferred from
        /// <see cref="ReplaceTemplate"/>. The two are independent: an <see cref="SearchVerb.Apply"/> with an
        /// empty template is well-defined (it replaces each match with nothing), so this model has no invalid
        /// verb/template combination to guard.
        /// </summary>
        public SearchVerb Verb { get; set; } = SearchVerb.Match;

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
        /// Flags controlling how <see cref="ReplaceTemplate"/> is interpreted when replacements are applied
        /// or previewed (default <see cref="RegExFormatFlags.Perl"/>). A single raw mask handed straight to
        /// the engine — a front-end ORs the individual flags it wants (e.g. <see cref="RegExFormatFlags.Sed"/>
        /// for sed replacement syntax). The engine rejects flags it does not support (notably boost's
        /// whole-template "literal" flag, which is deliberately not exposed — escape the template instead).
        /// </summary>
        public RegExFormatFlags FormatFlags { get; set; } = RegExFormatFlags.Perl;

        /// <summary>
        /// What to do with a directory encountered during the search — applied uniformly to both
        /// directories named on input and directories discovered while recursing, after the
        /// <see cref="DirectoryFilters"/> verdict (a directory the filters exclude is pruned silently,
        /// regardless of this). Defaults to <see cref="DirectoryDisposition.Error"/>.
        /// </summary>
        public DirectoryDisposition Directories { get; set; } = DirectoryDisposition.Error;

        /// <summary>
        /// When true (the default), files that detection judges to be binary are skipped. Set false to
        /// search them anyway (their <see cref="SearchFile.LooksBinary"/> is still reported, so a caller
        /// that wants different handling — e.g. treat binary as an error — can do so from
        /// <see cref="ISearchSink.OnFile"/>).
        /// </summary>
        public bool SkipBinaryFiles { get; set; } = true;

        /// <summary>Which encoding/binary detection steps to run (default: <see cref="EncodingDetectionOptions.Default"/> — all steps).</summary>
        public EncodingDetectionOptions EncodingDetection { get; set; } = EncodingDetectionOptions.Default;

        /// <summary>
        /// The maximum number of files processed concurrently. <b>1</b> (the default) processes files
        /// serially, preserving deterministic per-file ordering. <b>0</b> means "automatic" — the engine
        /// picks a reasonable degree (currently <see cref="System.Environment.ProcessorCount"/>). Any value
        /// &gt; 1 caps concurrency at that number. The same value applies to both search and replace.
        /// <para>
        /// Under concurrency, files interleave: the sink's callbacks are still serialized (never called
        /// concurrently), but a file's <see cref="ISearchSink.OnFile"/>…hits…<see cref="ISearchSink.OnFileComplete"/>
        /// bracket lets a front-end keep each file's output contiguous. Global ordering across files is the
        /// sink's concern (buffer and sort, or use 1). The compiled regex is shared across workers (it is
        /// immutable and free-threaded), so no per-file compilation cost is incurred.
        /// </para>
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = 1;

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
                FormatFlags = FormatFlags,
                Directories = Directories,
                SkipBinaryFiles = SkipBinaryFiles,
                EncodingDetection = EncodingDetection,
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                Pattern = Pattern,
            };

            copy.FileNameFilters.AddRange(FileNameFilters);
            copy.DirectoryFilters.AddRange(DirectoryFilters);
            copy.Paths.AddRange(Paths);
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
        /// <para>
        /// When <see cref="Pattern"/> is non-empty this compiles it (with <see cref="SyntaxFlags"/>) to
        /// verify it is a valid expression, reporting <see cref="SearchRequestProblem.PatternInvalid"/> if
        /// not; the compiled regex is discarded (this is a check, nothing is cached). A front-end can call
        /// this before constructing a <see cref="Engine.SearchJob"/> so an invalid pattern is surfaced up
        /// front rather than faulting the run.
        /// </para>
        /// </summary>
        public IReadOnlyList<SearchRequestProblem> Validate()
        {
            var problems = new List<SearchRequestProblem>();
            if (Pattern.Length == 0)
            {
                problems.Add(SearchRequestProblem.PatternRequired);
            }
            else if (!TryCompilePattern(out var patternError))
            {
                lastPatternError = patternError;
                problems.Add(SearchRequestProblem.PatternInvalid);
            }

            if (Paths.Count == 0)
            {
                problems.Add(SearchRequestProblem.PathRequired);
            }

            if (!CodePages.IsSupported(ResolvedDefaultCodePage))
            {
                problems.Add(SearchRequestProblem.UnsupportedCodePage);
            }

            if (!RegEx.MatchFlagsAreValid(MatchFlags))
            {
                problems.Add(SearchRequestProblem.InvalidMatchFlags);
            }

            if (!RegEx.FormatFlagsAreValid(FormatFlags))
            {
                problems.Add(SearchRequestProblem.InvalidFormatFlags);
            }

            if (MaxDegreeOfParallelism < 0)
            {
                problems.Add(SearchRequestProblem.InvalidParallelism);
            }

            if (!Enum.IsDefined(typeof(SearchVerb), Verb))
            {
                problems.Add(SearchRequestProblem.InvalidVerb);
            }

            if (!Enum.IsDefined(typeof(DirectoryDisposition), Directories))
            {
                problems.Add(SearchRequestProblem.InvalidDirectoryDisposition);
            }

            // EncodingDetection is a [Flags] set, so Enum.IsDefined can't validate a combination; reject
            // any bit outside the known steps instead.
            if ((EncodingDetection.Steps & ~EncodingDetectionSteps.All) != 0)
            {
                problems.Add(SearchRequestProblem.InvalidEncodingDetection);
            }

            return problems;
        }

        // Compiles Pattern with SyntaxFlags purely to check validity, disposing the result immediately
        // (nothing is cached -- see Validate). Returns true if it compiled; otherwise sets nativeMessage to
        // the engine's error text for DescribeProblemForCommandLine.
        private bool TryCompilePattern(out string? nativeMessage)
        {
            try
            {
                using (RegEx.Create(Pattern, SyntaxFlags))
                {
                }

                nativeMessage = null;
                return true;
            }
            catch (RegExException ex)
            {
                nativeMessage = ex.NativeMessage;
                return false;
            }
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
                SearchRequestProblem.PatternInvalid =>
                    lastPatternError == null ? "invalid pattern" : $"invalid pattern: {lastPatternError}",
                SearchRequestProblem.PathRequired => "no paths given",
                SearchRequestProblem.UnsupportedCodePage =>
                    $"unsupported encoding '{CodePages.GetName(ResolvedDefaultCodePage)}'",
                SearchRequestProblem.InvalidMatchFlags => "invalid match flags",
                SearchRequestProblem.InvalidFormatFlags => "invalid format flags",
                SearchRequestProblem.InvalidParallelism => "parallelism must not be negative",
                SearchRequestProblem.InvalidVerb => "invalid operation",
                SearchRequestProblem.InvalidDirectoryDisposition => "invalid directory handling",
                SearchRequestProblem.InvalidEncodingDetection => "invalid encoding-detection steps",
            };
#pragma warning restore CS8524
    }

    /// <summary>A way in which a <see cref="SearchRequest"/> can be invalid.</summary>
    public enum SearchRequestProblem
    {
        /// <summary>No <see cref="SearchRequest.Pattern"/> was given.</summary>
        PatternRequired,

        /// <summary><see cref="SearchRequest.Pattern"/> is not a valid regular expression for the chosen <see cref="SearchRequest.SyntaxFlags"/>.</summary>
        PatternInvalid,

        /// <summary>No <see cref="SearchRequest.Paths"/> were given.</summary>
        PathRequired,

        /// <summary><see cref="SearchRequest.ResolvedDefaultCodePage"/> is not one the engine can decode.</summary>
        UnsupportedCodePage,

        /// <summary><see cref="SearchRequest.MatchFlags"/> contains bits the engine does not accept.</summary>
        InvalidMatchFlags,

        /// <summary><see cref="SearchRequest.FormatFlags"/> contains bits the engine does not accept.</summary>
        InvalidFormatFlags,

        /// <summary><see cref="SearchRequest.MaxDegreeOfParallelism"/> is negative.</summary>
        InvalidParallelism,

        /// <summary><see cref="SearchRequest.Verb"/> is not a defined <see cref="SearchVerb"/> value.</summary>
        InvalidVerb,

        /// <summary><see cref="SearchRequest.Directories"/> is not a defined <see cref="DirectoryDisposition"/> value.</summary>
        InvalidDirectoryDisposition,

        /// <summary><see cref="SearchRequest.EncodingDetection"/> selects detection steps that are not defined.</summary>
        InvalidEncodingDetection,
    }

    /// <summary>The operation a <see cref="SearchRequest"/> performs — the engine's two actions.</summary>
    public enum SearchVerb
    {
        /// <summary>Find matches and report them (no file modification). A replacement template, if set, is
        /// still formatted and offered as a preview, but nothing is written.</summary>
        Match,

        /// <summary>Write replacements to files in place, applying <see cref="SearchRequest.ReplaceTemplate"/>
        /// to each match (an empty template deletes matches).</summary>
        Apply,
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
