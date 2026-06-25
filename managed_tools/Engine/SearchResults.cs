namespace UnicodeRegEx.Tools.Engine
{
    /// <summary>
    /// Receives results and status from a <see cref="SearchJob"/> run as they happen, so a
    /// front-end can stream output (CLI) or update a live view (GUI). Implementations should be
    /// cheap and should not throw. The job serializes its callbacks, so a sink need not be thread-safe.
    /// </summary>
    public interface ISearchSink
    {
        /// <summary>
        /// A file is about to be searched. Reported once per processed file — including files with no
        /// matches — and before any of that file's hits, so a front-end can learn its detected encoding
        /// and binary verdict even when it produces no hits. Skipped, errored, and empty files are not
        /// reported here.
        /// </summary>
        void OnFile(SearchFile file);

        /// <summary>A search result (a match) or a replace-preview result (a match and its replacement).</summary>
        void OnHit(in SearchHit hit);

        /// <summary>A file was rewritten in apply mode.</summary>
        void OnFileChanged(string path);

        /// <summary>A path could not be processed: a missing path, a directory access error, or a per-file failure.</summary>
        void OnError(string path, string message);
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

        /// <summary>True if detection judged the file to be binary (it was searched anyway).</summary>
        public bool LooksBinary { get; }
    }

    /// <summary>
    /// A single result. In search mode <see cref="Text"/> is the matched text and
    /// <see cref="Replacement"/> is null; in replace mode <see cref="Text"/> is the matched text and
    /// <see cref="Replacement"/> is what it becomes.
    /// </summary>
    /// <remarks>
    /// This shape is still evolving: byte offsets, surrounding context, etc. will be added when the
    /// GUI's needs are known. The engine does not track line numbers; mapping an offset to a line is a
    /// front-end concern.
    /// </remarks>
    public readonly struct SearchHit
    {
        public SearchHit(SearchFile file, string text, string? replacement)
        {
            File = file;
            Text = text;
            Replacement = replacement;
        }

        /// <summary>The file this hit is in (shared with the file's other hits and its <see cref="ISearchSink.OnFile"/> report).</summary>
        public SearchFile File { get; }

        /// <summary>The matched text.</summary>
        public string Text { get; }

        /// <summary>The replacement for the match, or null in search-only mode.</summary>
        public string? Replacement { get; }
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
