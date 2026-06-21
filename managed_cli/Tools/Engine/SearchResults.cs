namespace UnicodeRegEx.Tools.Engine
{
    /// <summary>
    /// Receives results and status from a <see cref="SearchEngine"/> run as they happen, so a
    /// front-end can stream output (CLI) or update a live view (GUI). Implementations should be
    /// cheap and should not throw.
    /// </summary>
    public interface ISearchSink
    {
        /// <summary>A search result (a matching line) or a replace-preview result (a match and its replacement).</summary>
        void OnHit(in SearchHit hit);

        /// <summary>A file was rewritten in apply mode.</summary>
        void OnFileChanged(string path);

        /// <summary>A path could not be processed: a missing path, a directory access error, or a per-file failure.</summary>
        void OnError(string path, string message);
    }

    /// <summary>
    /// A single result. In search mode <see cref="Text"/> is the matching line and
    /// <see cref="Replacement"/> is null; in replace mode <see cref="Text"/> is the matched text and
    /// <see cref="Replacement"/> is what it becomes.
    /// </summary>
    public readonly struct SearchHit
    {
        public SearchHit(string path, int line, string text, string? replacement)
        {
            Path = path;
            Line = line;
            Text = text;
            Replacement = replacement;
        }

        /// <summary>The file this hit is in.</summary>
        public string Path { get; }

        /// <summary>1-based line number of the match.</summary>
        public int Line { get; }

        /// <summary>The matching line (search) or the matched text (replace).</summary>
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
