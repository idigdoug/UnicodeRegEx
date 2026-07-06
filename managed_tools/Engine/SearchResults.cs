namespace UnicodeRegEx.Tools.Engine
{
    using System;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;

    /// <summary>
    /// A sink's response to a callback, letting it steer the run. Applies to <see cref="ISearchSink.OnFile"/>
    /// and <see cref="ISearchSink.OnMatch"/>.
    /// </summary>
    public enum SearchResponse
    {
        /// <summary>Stop processing the current file, but continue with the remaining files.</summary>
        StopFile,

        /// <summary>Stop the whole job (equivalent to <see cref="SearchJob.Cancel"/>).</summary>
        StopAll,

        /// <summary>Keep going.</summary>
        Continue,
    }

    /// <summary>The kind of action an <see cref="ISearchSink.OnApply"/> callback requests for a match.</summary>
    public enum ApplyActionKind
    {
        /// <summary>Abandon this file's rewrite (the current match is not written; the file is left untouched) and continue with the next file.</summary>
        StopFile,

        /// <summary>Abandon this file's rewrite and stop the whole run.</summary>
        StopAll,

        /// <summary>Write the match's formatted replacement (the request's template applied) — the default.</summary>
        Default,

        /// <summary>Write the matched input unchanged (leave this occurrence as-is).</summary>
        Original,

        /// <summary>Write nothing for this match (delete the matched text).</summary>
        Delete,

        /// <summary>Write caller-supplied bytes (a computed replacement); see <see cref="ApplyAction.CustomBytes"/>.</summary>
        Custom,
    }

    /// <summary>
    /// What an <see cref="ISearchSink.OnApply"/> callback tells the engine to do with a match while
    /// rewriting a file. Use the static members for the fixed actions (<see cref="Default"/>,
    /// <see cref="Original"/>, <see cref="Delete"/>, <see cref="StopFile"/>, <see cref="StopAll"/>) or
    /// <see cref="Custom(System.ArraySegment{byte})"/> to supply computed replacement bytes.
    /// </summary>
    public readonly struct ApplyAction
    {
        private ApplyAction(ApplyActionKind kind, ArraySegment<byte> customBytes)
        {
            Kind = kind;
            CustomBytes = customBytes;
        }

        /// <summary>The action to take.</summary>
        public ApplyActionKind Kind { get; }

        /// <summary>
        /// The bytes to write when <see cref="Kind"/> is <see cref="ApplyActionKind.Custom"/>; written to the
        /// output file verbatim (no encoding conversion), so the caller is responsible for producing them in
        /// the file's code page (available via the match/file). A default or empty segment writes nothing.
        /// Meaningless (and default) for the other kinds.
        /// </summary>
        public ArraySegment<byte> CustomBytes { get; }

        /// <summary>Write the match's formatted replacement (the request's template applied).</summary>
        public static ApplyAction Default { get; } = new ApplyAction(ApplyActionKind.Default, default);

        /// <summary>Write the matched input unchanged.</summary>
        public static ApplyAction Original { get; } = new ApplyAction(ApplyActionKind.Original, default);

        /// <summary>Write nothing for this match (delete it).</summary>
        public static ApplyAction Delete { get; } = new ApplyAction(ApplyActionKind.Delete, default);

        /// <summary>Abandon this file's rewrite and continue with the next file.</summary>
        public static ApplyAction StopFile { get; } = new ApplyAction(ApplyActionKind.StopFile, default);

        /// <summary>Abandon this file's rewrite and stop the whole run.</summary>
        public static ApplyAction StopAll { get; } = new ApplyAction(ApplyActionKind.StopAll, default);

        /// <summary>
        /// Write the given bytes for this match (a computed replacement). The bytes are written verbatim; a
        /// default or empty segment writes nothing (equivalent to <see cref="Delete"/>).
        /// </summary>
        public static ApplyAction Custom(ArraySegment<byte> bytes) => new ApplyAction(ApplyActionKind.Custom, bytes);
    }

    /// <summary>
    /// Receives results and status from a <see cref="SearchJob"/> run as they happen, so a
    /// front-end can stream output (CLI) or update a live view (GUI). Implementations should be cheap.
    /// A callback that throws faults the whole job (a thrown sink is treated as a bug, not a per-file error).
    /// <para>
    /// THREADING: the job does not serialize callbacks. A single file's callbacks (<see cref="OnFile"/> →
    /// its <see cref="OnMatch"/>/<see cref="OnApply"/> calls → <see cref="OnFileComplete"/>) always run on one thread, in order, so no
    /// synchronization is needed for per-file state (carry it via <see cref="SearchFile.Context"/>). Under
    /// <see cref="SearchRequest.MaxDegreeOfParallelism"/> &gt; 1, callbacks for DIFFERENT files may run
    /// concurrently on different threads, so an implementation must make any state it shares across files
    /// thread-safe. At the default degree of 1 everything is single-threaded.
    /// </para>
    /// </summary>
    public interface ISearchSink
    {
        /// <summary>
        /// A file is about to be searched. Reported once per processed file — including files with no
        /// matches — and before any of that file's hits, so a front-end can learn its detected encoding
        /// and binary verdict even when it produces no hits. Skipped, errored, and empty files are not
        /// reported here. Return <see cref="SearchResponse.StopFile"/> to skip this file, or
        /// <see cref="SearchResponse.StopAll"/> to end the run.
        /// <para>
        /// <paramref name="fileBytes"/> is the file's raw content (the same fileBytes the engine is about to
        /// search), so a sink can inspect it to make its own decisions — e.g. call
        /// <see cref="SearchFile.OverrideCodePage"/> to re-decode with a different code page, or record a
        /// hint in <see cref="SearchFile.Context"/>. Like <see cref="SearchHit"/>, it is a
        /// <see langword="ref"/> struct valid only for the duration of this call; a sink must not store it.
        /// </para>
        /// </summary>
        SearchResponse OnFile(SearchFile file, RegExPinnedBytes fileBytes);

        /// <summary>
        /// A file that was reported by <see cref="OnFile"/> (and not skipped by it) has finished being
        /// processed — after its last hit. Paired one-to-one with a non-skipping <see cref="OnFile"/>:
        /// it fires exactly for files whose <see cref="OnFile"/> returned <see cref="SearchResponse.Continue"/>,
        /// and never for skipped/errored/empty files (which never raised <see cref="OnFile"/> at all).
        /// Under parallel processing, files interleave; this bracket lets a sink group a file's hits (e.g.
        /// buffer them at <see cref="OnFile"/> and flush the buffer here) so per-file output stays contiguous.
        /// </summary>
        void OnFileComplete(SearchFile file);

        /// <summary>
        /// A match found by the <see cref="SearchVerb.Match"/> verb (search). The match's replacement is
        /// available as a preview via <see cref="SearchHit.Replacement"/>, but nothing is written. Return
        /// <see cref="SearchResponse.StopFile"/> to stop enumerating this file (e.g. after N matches), or
        /// <see cref="SearchResponse.StopAll"/> to end the run. Fires only for the search verb.
        /// </summary>
        SearchResponse OnMatch(in SearchHit hit);

        /// <summary>
        /// A match found by the <see cref="SearchVerb.Apply"/> verb (rewrite). The returned
        /// <see cref="ApplyAction"/> tells the engine what to write for this match: the default formatted
        /// replacement, the original text unchanged, nothing (delete), or caller-supplied bytes (a computed
        /// replacement) — or to abandon the file's rewrite. The engine owns the crash-safe, atomic,
        /// encoding-preserving write; the callback only chooses <i>what</i>. Fires only for the apply verb.
        /// </summary>
        ApplyAction OnApply(in SearchHit hit);

        /// <summary>A file was rewritten in apply mode.</summary>
        void OnFileChanged(string path);

        /// <summary>A path could not be processed: a missing path, a directory access error, or a per-file failure. Receives the underlying exception so the sink can present or classify it as it sees fit.</summary>
        void OnError(string path, Exception exception);
    }

    /// <summary>
    /// A convenience base for <see cref="ISearchSink"/> that implements every callback as an inert default:
    /// the steering callbacks (<see cref="OnFile"/>, <see cref="OnMatch"/>, <see cref="OnApply"/>) return
    /// <see cref="SearchResponse.Continue"/> and the notification callbacks do nothing. Derive from it and
    /// override only the callbacks you care about — useful for a sink that, say, only reacts to hits.
    /// </summary>
    /// <remarks>
    /// TRADEOFF: because this supplies a default for every member, a callback added to
    /// <see cref="ISearchSink"/> in a future version is <b>silently no-op'd</b> on derived sinks rather than
    /// producing a compile error. That is convenient but can hide a callback you would have wanted to handle.
    /// If you prefer the compiler to flag new callbacks so you consciously handle them, implement
    /// <see cref="ISearchSink"/> directly instead of deriving from this.
    /// </remarks>
    public abstract class SearchSinkBase : ISearchSink
    {
        /// <inheritdoc/>
        public virtual SearchResponse OnFile(SearchFile file, RegExPinnedBytes fileBytes) => SearchResponse.Continue;

        /// <inheritdoc/>
        public virtual void OnFileComplete(SearchFile file)
        {
        }

        /// <inheritdoc/>
        public virtual SearchResponse OnMatch(in SearchHit hit) => SearchResponse.Continue;

        /// <inheritdoc/>
        public virtual ApplyAction OnApply(in SearchHit hit) => ApplyAction.Default;

        /// <inheritdoc/>
        public virtual void OnFileChanged(string path)
        {
        }

        /// <inheritdoc/>
        public virtual void OnError(string path, Exception exception)
        {
        }
    }

    /// <summary>
    /// A file that the engine searched, with the metadata detection produced for it. Created once per
    /// processed file and shared (by reference) with every <see cref="SearchHit"/> from that file, so a
    /// hit always knows the file it came from even when results from several files interleave.
    /// </summary>
    /// <remarks>
    /// A sink may, during <see cref="ISearchSink.OnFile"/>, adjust how the file is processed via
    /// <see cref="OverrideCodePage"/> and attach arbitrary per-file state via <see cref="Context"/>. Once
    /// <see cref="ISearchSink.OnFile"/> returns, the file is <see cref="IsLocked">locked</see> and
    /// <see cref="OverrideCodePage"/> throws (the code page is consumed to decode the file immediately
    /// after). The engine invokes <see cref="ISearchSink.OnFile"/>, <see cref="ISearchSink.OnMatch"/>/<see cref="ISearchSink.OnApply"/>, and
    /// <see cref="ISearchSink.OnFileComplete"/> for a given file on the same thread, so a sink needs no
    /// synchronization to thread state through <see cref="Context"/> across them.
    /// </remarks>
    public sealed class SearchFile
    {
        private int codePage;

        public SearchFile(string path, int codePage, bool looksBinary)
        {
            Path = path;
            this.codePage = codePage;
            LooksBinary = looksBinary;
        }

        /// <summary>The full path of the file.</summary>
        public string Path { get; }

        /// <summary>
        /// The code page the file is decoded with. Reflects detection, or a sink's
        /// <see cref="OverrideCodePage"/> if one was applied during <see cref="ISearchSink.OnFile"/>.
        /// </summary>
        public int CodePage => codePage;

        /// <summary>
        /// True if detection judged the file to be binary. This is the detector's verdict and is read-only;
        /// a sink that disagrees can record its own opinion via <see cref="Context"/> (the engine does not
        /// act on it) and/or return <see cref="SearchResponse.StopFile"/> from <see cref="ISearchSink.OnFile"/>.
        /// </summary>
        public bool LooksBinary { get; }

        /// <summary>
        /// Arbitrary per-file state a sink may attach (typically during <see cref="ISearchSink.OnFile"/>) to
        /// carry information forward to the file's <see cref="ISearchSink.OnMatch"/>/<see cref="ISearchSink.OnApply"/> and
        /// <see cref="ISearchSink.OnFileComplete"/> callbacks. The engine never reads or interprets this;
        /// it is purely a channel for the sink's own use. Not locked — a sink may set it whenever it likes.
        /// </summary>
        public object? Context { get; set; }

        /// <summary>
        /// True once <see cref="ISearchSink.OnFile"/> has returned and the file's code page has been
        /// committed. After this, <see cref="OverrideCodePage"/> throws.
        /// </summary>
        public bool IsLocked { get; private set; }

        /// <summary>
        /// Overrides the code page the engine will decode this file with. Callable only from within
        /// <see cref="ISearchSink.OnFile"/> (before the file is locked); the new code page takes effect for
        /// the file's search/replace immediately after <see cref="ISearchSink.OnFile"/> returns.
        /// </summary>
        /// <param name="codePage">
        /// The code page to decode with. The CP_ACP sentinel (<see cref="RegExCodePage.SystemDefault"/>) is
        /// resolved to the system ANSI code page.
        /// </param>
        /// <exception cref="InvalidOperationException">The file is already locked (called outside <see cref="ISearchSink.OnFile"/>).</exception>
        /// <exception cref="ArgumentException">The (resolved) code page is not one the engine can decode.</exception>
        public void OverrideCodePage(int codePage)
        {
            if (IsLocked)
            {
                throw new InvalidOperationException(
                    "The code page can only be overridden from within ISearchSink.OnFile, before the file is locked.");
            }

            var resolved = CodePages.ResolveDefault(codePage);
            if (!CodePages.IsSupported(resolved))
            {
                throw new ArgumentException($"Code page {codePage} is not supported.", nameof(codePage));
            }

            this.codePage = resolved;
        }

        // Commits the code page and prevents further OverrideCodePage calls. Called by the engine right
        // after ISearchSink.OnFile returns.
        internal void Lock() => IsLocked = true;
    }

    /// <summary>
    /// A single match. Carries the file it belongs to and the underlying <see cref="RegExMatch"/>, from
    /// which a tool derives matched text, sub-matches, byte offsets, and surrounding context.
    /// </summary>
    /// <remarks>
    /// LIFETIME: a <see cref="SearchHit"/> (and its <see cref="Match"/>) is valid ONLY for the duration
    /// of the <see cref="ISearchSink.OnMatch"/>/<see cref="ISearchSink.OnApply"/> call that receives it. The match enumerator advances a
    /// shared native object on each step, so the match goes stale on the next iteration, and the file's
    /// fileBytes are unmapped when the file finishes. A sink that needs to keep anything (text, offsets,
    /// context fileBytes) must copy it out during the call. Being a <see langword="ref"/> struct, the hit
    /// cannot be stored in a field or collection, which enforces this at compile time.
    /// </remarks>
    public readonly ref struct SearchHit
    {
        public SearchHit(SearchFile file, RegExMatch match)
        {
            File = file;
            Match = match;
        }

        /// <summary>The file this hit is in (shared with the file's other hits and its <see cref="ISearchSink.OnFile"/> report).</summary>
        public SearchFile File { get; }

        /// <summary>The underlying match: sub-matches, byte offsets, input fileBytes, formatting.</summary>
        public RegExMatch Match { get; }

        /// <summary>The matched text (sub-match 0), decoded with the file's code page.</summary>
        public string Text => Match.Text;

        /// <summary>
        /// The replacement this match formats to under the run's replacement template, computed on access.
        /// The template is always applied (an empty template formats to an empty string), so this is never
        /// null — a preview works the same whether the verb is <see cref="SearchVerb.Match"/> or
        /// <see cref="SearchVerb.Apply"/> and regardless of whether the template is empty.
        /// </summary>
        public string Replacement => Match.Format();
    }

    /// <summary>The aggregate outcome of a <see cref="SearchEngine"/> run.</summary>
    public readonly struct SearchSummary
    {
        public SearchSummary(bool anyMatch, int filesChanged, int errors, bool cancelled)
        {
            AnyMatch = anyMatch;
            FilesChanged = filesChanged;
            Errors = errors;
            Cancelled = cancelled;
        }

        /// <summary>True if any file matched, or (in apply mode) was changed.</summary>
        public bool AnyMatch { get; }

        /// <summary>Number of files rewritten (apply mode).</summary>
        public int FilesChanged { get; }

        /// <summary>Number of files that failed to process.</summary>
        public int Errors { get; }

        /// <summary>True if the run was cancelled before completing.</summary>
        public bool Cancelled { get; }
    }
}
