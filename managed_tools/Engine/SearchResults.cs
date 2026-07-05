namespace UnicodeRegEx.Tools.Engine
{
    using System;
    using UnicodeRegEx;

    /// <summary>
    /// A sink's response to a callback, letting it steer the run. Applies to <see cref="ISearchSink.OnFile"/>
    /// and <see cref="ISearchSink.OnHit"/>.
    /// </summary>
    public enum SearchResponse
    {
        /// <summary>Keep going.</summary>
        Continue,

        /// <summary>Stop processing the current file, but continue with the remaining files.</summary>
        StopFile,

        /// <summary>Stop the whole job (equivalent to <see cref="SearchJob.Cancel"/>).</summary>
        StopAll,
    }

    /// <summary>
    /// Receives results and status from a <see cref="SearchJob"/> run as they happen, so a
    /// front-end can stream output (CLI) or update a live view (GUI). Implementations should be
    /// cheap. The job serializes its callbacks, so a sink need not be thread-safe. A callback that
    /// throws faults the whole job (a thrown sink is treated as a bug, not a per-file error).
    /// </summary>
    public interface ISearchSink
    {
        /// <summary>
        /// A file is about to be searched. Reported once per processed file — including files with no
        /// matches — and before any of that file's hits, so a front-end can learn its detected encoding
        /// and binary verdict even when it produces no hits. Skipped, errored, and empty files are not
        /// reported here. Return <see cref="SearchResponse.StopFile"/> to skip this file, or
        /// <see cref="SearchResponse.StopAll"/> to end the run.
        /// </summary>
        SearchResponse OnFile(SearchFile file);

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
        /// A search result (a match) or a replace-preview result (a match and its replacement). Return
        /// <see cref="SearchResponse.StopFile"/> to stop enumerating this file (e.g. after N matches), or
        /// <see cref="SearchResponse.StopAll"/> to end the run.
        /// </summary>
        SearchResponse OnHit(in SearchHit hit);

        /// <summary>A file was rewritten in apply mode.</summary>
        void OnFileChanged(string path);

        /// <summary>A path could not be processed: a missing path, a directory access error, or a per-file failure. Receives the underlying exception so the sink can present or classify it as it sees fit.</summary>
        void OnError(string path, Exception exception);
    }

    /// <summary>
    /// A convenience base for <see cref="ISearchSink"/> that implements every callback as an inert default:
    /// the steering callbacks (<see cref="OnFile"/>, <see cref="OnHit"/>) return
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
        public virtual SearchResponse OnFile(SearchFile file) => SearchResponse.Continue;

        /// <inheritdoc/>
        public virtual void OnFileComplete(SearchFile file)
        {
        }

        /// <inheritdoc/>
        public virtual SearchResponse OnHit(in SearchHit hit) => SearchResponse.Continue;

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
    public sealed class SearchFile
    {
        public SearchFile(string path, int codePage, bool looksBinary)
        {
            Path = path;
            CodePage = codePage;
            LooksBinary = looksBinary;
        }

        /// <summary>The full path of the file.</summary>
        public string Path { get; }

        /// <summary>The code page the file was decoded with.</summary>
        public int CodePage { get; }

        /// <summary>True if detection judged the file to be binary.</summary>
        public bool LooksBinary { get; }
    }

    /// <summary>
    /// A single match. Carries the file it belongs to and the underlying <see cref="RegExMatch"/>, from
    /// which a tool derives matched text, sub-matches, byte offsets, and surrounding context.
    /// </summary>
    /// <remarks>
    /// LIFETIME: a <see cref="SearchHit"/> (and its <see cref="Match"/>) is valid ONLY for the duration
    /// of the <see cref="ISearchSink.OnHit"/> call that receives it. The match enumerator advances a
    /// shared native object on each step, so the match goes stale on the next iteration, and the file's
    /// bytes are unmapped when the file finishes. A sink that needs to keep anything (text, offsets,
    /// context bytes) must copy it out during the call. Being a <see langword="ref"/> struct, the hit
    /// cannot be stored in a field or collection, which enforces this at compile time.
    /// </remarks>
    public readonly ref struct SearchHit
    {
        private readonly bool isReplace;

        public SearchHit(SearchFile file, RegExMatch match, bool isReplace)
        {
            File = file;
            Match = match;
            this.isReplace = isReplace;
        }

        /// <summary>The file this hit is in (shared with the file's other hits and its <see cref="ISearchSink.OnFile"/> report).</summary>
        public SearchFile File { get; }

        /// <summary>The underlying match: sub-matches, byte offsets, input bytes, formatting.</summary>
        public RegExMatch Match { get; }

        /// <summary>The matched text (sub-match 0), decoded with the file's code page.</summary>
        public string Text => Match.Text;

        /// <summary>
        /// The replacement this match formats to in replace mode, or <see langword="null"/> in
        /// search-only mode. Computed on access (search-mode hits never format).
        /// </summary>
        public string? Replacement => isReplace ? Match.Format() : null;
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
