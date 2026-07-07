namespace UnicodeRegEx.Tools.Collecting
{
    using System;
    using System.Collections.Generic;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools.Engine;

    /// <summary>
    /// An <see cref="ISearchSink"/> that copies each match out of its (ref struct, call-lifetime)
    /// <see cref="SearchHit"/> into a storable <see cref="HitRecord"/>, accumulating them for a find/replace
    /// UI to browse after the run. Reusable and front-end-neutral: it holds no UI state and is internally
    /// thread-safe, so it works under parallel processing.
    /// <para>
    /// THREADING: the engine may invoke callbacks on worker threads (and, under parallelism, for different
    /// files concurrently). The <see cref="Hits"/> list and error list are lock-guarded, so a consumer may
    /// read <see cref="Hits"/> at any time. <see cref="HitsAdded"/> is raised on whichever thread produced
    /// the hits (throttled), so a UI subscriber must marshal to its own thread.
    /// </para>
    /// </summary>
    public sealed class CollectingSink : SearchSinkBase
    {
        // A bounded window of file bytes captured on each side of a match, for display and (later) staleness
        // re-verification. Hard-coded for now; a match near the file start/end yields a shorter window.
        private const int ContextByteCount = 64;

        // Coalesce HitsAdded so a fast scan does not flood a UI: raise at most this often, or once this many
        // new hits have accumulated, whichever comes first.
        private const int ThrottleMs = 100;
        private const int ThrottleCount = 256;

        private readonly object gate = new object();
        private readonly List<HitRecord> hits = new List<HitRecord>();
        private readonly List<SearchError> errors = new List<SearchError>();

        private int lastRaisedTick;
        private int pendingSinceRaise;

        /// <summary>
        /// Raised (throttled) as hits accumulate so a UI can update during the scan. Read <see cref="Hits"/>
        /// on the event and append any records past the count you last displayed. May fire on a worker
        /// thread — marshal to the UI thread before touching controls.
        /// </summary>
        public event EventHandler? HitsAdded;

        /// <summary>
        /// A thread-safe snapshot of the hits collected so far, in the order they were found. Append-only:
        /// a consumer can track how many it has already shown and take the rest.
        /// </summary>
        public IReadOnlyList<HitRecord> Hits
        {
            get
            {
                lock (gate)
                {
                    return hits.ToArray();
                }
            }
        }

        /// <summary>A thread-safe snapshot of the errors reported during the run.</summary>
        public IReadOnlyList<SearchError> Errors
        {
            get
            {
                lock (gate)
                {
                    return errors.ToArray();
                }
            }
        }

        /// <inheritdoc/>
        public override SearchResponse OnFile(SearchFile file, RegExPinnedBytes fileBytes)
        {
            // One memory stream per file, reused across the file's hits to format each replacement. Held on
            // the file's Context (race-free: a single file's callbacks run on one thread) and disposed in
            // OnFileComplete.
            file.Context = RegEx.CreateMemoryStream();
            return SearchResponse.Continue;
        }

        /// <inheritdoc/>
        public override void OnFileComplete(SearchFile file)
        {
            (file.Context as RegExMemoryStream)?.Dispose();
            file.Context = null;
        }

        /// <inheritdoc/>
        public override SearchResponse OnMatch(in SearchHit hit)
        {
            var record = CaptureHit(hit, (RegExMemoryStream)hit.File.Context!);

            bool raise;
            lock (gate)
            {
                hits.Add(record);
                pendingSinceRaise++;
                raise = ShouldRaise();
            }

            if (raise)
            {
                HitsAdded?.Invoke(this, EventArgs.Empty);
            }

            return SearchResponse.Continue;
        }

        /// <inheritdoc/>
        public override void OnError(string path, Exception exception)
        {
            lock (gate)
            {
                errors.Add(new SearchError(path, exception));
            }
        }

        // Copies the match and its clamped byte context out of the (call-lifetime) hit into a HitRecord.
        private HitRecord CaptureHit(in SearchHit hit, RegExMemoryStream replacementStream)
        {
            var input = hit.Match.Input;
            var whole = hit.Match.GetSubMatch(0);
            var matchBegin = whole.Begin;
            var matchEnd = whole.End;

            // Clamp the context windows at the file's start and end.
            var preLength = matchBegin < (nuint)ContextByteCount ? matchBegin : (nuint)ContextByteCount;
            var available = input.Size - matchEnd;
            var postLength = available < (nuint)ContextByteCount ? available : (nuint)ContextByteCount;

            var preMatch = input.Slice(matchBegin - preLength, preLength).ToArray();
            var matchBytes = input.Slice(matchBegin, whole.Size).ToArray();
            var postMatch = input.Slice(matchEnd, postLength).ToArray();

            // Format the replacement into the reused per-file stream (reset first), then snapshot its bytes.
            replacementStream.Reset();
            hit.Match.FormatTo(replacementStream, hit.File.CodePage);
            var replacementBytes = replacementStream.Buffer.ToArray();

            return new HitRecord(hit.File, matchBegin, preMatch, matchBytes, postMatch, replacementBytes);
        }

        // Decide whether to raise HitsAdded now (caller holds the lock).
        private bool ShouldRaise()
        {
            var now = Environment.TickCount;
            if (pendingSinceRaise >= ThrottleCount || unchecked(now - lastRaisedTick) >= ThrottleMs)
            {
                lastRaisedTick = now;
                pendingSinceRaise = 0;
                return true;
            }

            return false;
        }
    }

    /// <summary>A path that failed to process, with the underlying exception, as collected by <see cref="CollectingSink"/>.</summary>
    public readonly struct SearchError
    {
        public SearchError(string path, Exception exception)
        {
            Path = path;
            Exception = exception;
        }

        /// <summary>The path that could not be processed.</summary>
        public string Path { get; }

        /// <summary>The underlying failure.</summary>
        public Exception Exception { get; }
    }
}
