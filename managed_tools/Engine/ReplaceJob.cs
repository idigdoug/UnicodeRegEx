namespace UnicodeRegEx.Tools.Engine
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.MemoryMappedFiles;
    using System.Threading;
    using System.Threading.Tasks;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools.Collecting;

    /// <summary>
    /// Applies an already-known set of matches to files — the "apply the checked results" operation for the
    /// find/replace UI. Unlike <see cref="SearchJob"/> it does not enumerate a tree or re-run the regex: it is
    /// given the <see cref="HitRecord"/>s the user chose in a preview, groups them by file, memory-maps each
    /// file, re-verifies each match's captured context still surrounds its offset (a staleness guard against
    /// the file changing since the preview), and rewrites the file via <see cref="RegExFileStream"/> — copying
    /// the unchanged spans verbatim and writing each chosen match's captured
    /// <see cref="HitRecord.ReplacementBytes"/> (so the result is exactly what the preview showed; no regex
    /// re-run, no re-format). A match whose context no longer matches is left unchanged and counted as
    /// skipped-stale; unchosen matches are never touched.
    /// <para>
    /// Shaped like <see cref="SearchJob"/> (<see cref="RunAsync"/> / <see cref="Cancel"/> /
    /// <see cref="ProgressChanged"/> / counts) and shares its parallelism model: files are rewritten
    /// concurrently per <c>maxDegreeOfParallelism</c> (same meaning as
    /// <see cref="SearchRequest.MaxDegreeOfParallelism"/>), and cancellation is honored at file boundaries.
    /// Mid-file cancellation (the write stream supports it via <c>LinkCancellation</c>) is a later refinement.
    /// </para>
    /// </summary>
    public sealed class ReplaceJob : IDisposable
    {
        private readonly object gate = new object();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        // Chosen matches grouped by file, each group sorted ascending by offset.
        private readonly List<KeyValuePair<SearchFile, List<HitRecord>>> byFile;

        // How many files to rewrite concurrently: 0 = automatic (ProcessorCount), 1 = serial, >1 = capped.
        // Shares the meaning of SearchRequest.MaxDegreeOfParallelism.
        private readonly int maxDegreeOfParallelism;

        private readonly List<SearchError> errors = new List<SearchError>();
        private readonly List<string> changedFiles = new List<string>();

        private int totalFileCount;
        private int completedFileCount;
        private int appliedCount;
        private int skippedStaleCount;
        private volatile SearchJobState state = SearchJobState.Created;
        private int started;
        private bool disposed;

        /// <summary>Creates a job that will apply the given chosen matches when <see cref="RunAsync"/> is called.</summary>
        /// <param name="selectedHits">The matches the user chose to apply.</param>
        /// <param name="maxDegreeOfParallelism">
        /// How many files to rewrite concurrently, sharing the meaning of
        /// <see cref="SearchRequest.MaxDegreeOfParallelism"/>: 0 = automatic (processor count), 1 = serial
        /// (the default), any value &gt; 1 caps concurrency. Files are independent (each is mapped and
        /// rewritten on its own), so this parallelizes cleanly.
        /// </param>
        public ReplaceJob(IEnumerable<HitRecord> selectedHits, int maxDegreeOfParallelism = 1)
        {
            if (selectedHits == null)
            {
                throw new ArgumentNullException(nameof(selectedHits));
            }

            this.maxDegreeOfParallelism = maxDegreeOfParallelism;

            // Group by file (preserving first-seen order); sort each file's hits by offset so the rewrite
            // walks the file front-to-back. The SearchFile instance is the key: all of a file's hits from one
            // search run share it (reference equality), and it carries the path (and code page) directly.
            var groups = new Dictionary<SearchFile, List<HitRecord>>();
            var order = new List<SearchFile>();
            foreach (var hit in selectedHits)
            {
                if (!groups.TryGetValue(hit.File, out var list))
                {
                    list = new List<HitRecord>();
                    groups[hit.File] = list;
                    order.Add(hit.File);
                }

                list.Add(hit);
            }

            byFile = new List<KeyValuePair<SearchFile, List<HitRecord>>>(order.Count);
            foreach (var file in order)
            {
                var list = groups[file];
                list.Sort((a, b) => a.MatchFileOffset < b.MatchFileOffset ? -1 : (a.MatchFileOffset > b.MatchFileOffset ? 1 : 0));
                byFile.Add(new KeyValuePair<SearchFile, List<HitRecord>>(file, list));
            }

            totalFileCount = byFile.Count;
        }

        /// <summary>The job's lifecycle phase.</summary>
        public SearchJobState State => state;

        /// <summary>The number of files to rewrite (one per file that has at least one chosen match).</summary>
        public int TotalFileCount => Volatile.Read(ref totalFileCount);

        /// <summary>The number of files processed so far.</summary>
        public int CompletedFileCount => Volatile.Read(ref completedFileCount);

        /// <summary>The number of chosen matches actually replaced (context re-verified).</summary>
        public int AppliedCount { get { lock (gate) { return appliedCount; } } }

        /// <summary>The number of chosen matches skipped because the file had changed since the preview.</summary>
        public int SkippedStaleCount { get { lock (gate) { return skippedStaleCount; } } }

        /// <summary>A thread-safe snapshot of the files rewritten during the run.</summary>
        public IReadOnlyList<string> ChangedFiles { get { lock (gate) { return changedFiles.ToArray(); } } }

        /// <summary>A thread-safe snapshot of the errors reported during the run.</summary>
        public IReadOnlyList<SearchError> Errors { get { lock (gate) { return errors.ToArray(); } } }

        /// <summary>The job's cancellation token, signaled by <see cref="Cancel"/>.</summary>
        public CancellationToken CancellationToken => cancellation.Token;

        /// <summary>
        /// Raised when <see cref="State"/> or <see cref="CompletedFileCount"/> changes. Carries no data — read
        /// the properties. Raised on the job's background thread, so a UI consumer must marshal.
        /// </summary>
        public event EventHandler? ProgressChanged;

        /// <summary>Requests cancellation; the job stops at the next file boundary.</summary>
        public void Cancel() => cancellation.Cancel();

        /// <summary>Runs the apply on a background thread. May be awaited once.</summary>
        public Task RunAsync()
        {
            if (Interlocked.Exchange(ref started, 1) != 0)
            {
                throw new InvalidOperationException("This job has already been started.");
            }

            return Task.Run(RunCore);
        }

        private void RunCore()
        {
            SetState(SearchJobState.Processing);

            var dop = ResolveDegreeOfParallelism();

            if (dop == 1)
            {
                foreach (var group in byFile)
                {
                    if (cancellation.IsCancellationRequested)
                    {
                        break;
                    }

                    ProcessOne(group);
                }
            }
            else
            {
                // Parallel path: files are independent (each mapped and rewritten on its own), so this
                // parallelizes without shared per-file state. The token stops in-flight workers at their next
                // file boundary; per-file failures are captured in ProcessOne (they never abort the loop).
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = dop,
                    CancellationToken = cancellation.Token,
                };

                try
                {
                    Parallel.ForEach(byFile, options, ProcessOne);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is reflected in the terminal state below.
                }
            }

            SetState(cancellation.IsCancellationRequested ? SearchJobState.Canceled : SearchJobState.Completed);
        }

        // Rewrites one file group: applies its chosen matches, capturing any per-file failure as an error, and
        // ticks progress. Safe to call concurrently — the shared counters/lists are interlocked or gate-locked.
        private void ProcessOne(KeyValuePair<SearchFile, List<HitRecord>> group)
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            try
            {
                ApplyFile(group.Key, group.Value);
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    errors.Add(new SearchError(group.Key.Path, ex));
                }
            }

            Interlocked.Increment(ref completedFileCount);
            RaiseProgress();
        }

        // Resolves maxDegreeOfParallelism the same way SearchJob does: 0 => automatic (ProcessorCount),
        // otherwise the requested value clamped to at least 1.
        private int ResolveDegreeOfParallelism()
        {
            if (maxDegreeOfParallelism == 0)
            {
                return Math.Max(1, Environment.ProcessorCount);
            }

            return Math.Max(1, maxDegreeOfParallelism);
        }

        // Rewrites a single file: verify each chosen match against its captured context, then stream the file
        // out with the valid matches replaced by their captured replacement bytes.
        private unsafe void ApplyFile(SearchFile file, List<HitRecord> hits)
        {
            var path = file.Path;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;

            if (length == 0)
            {
                // Nothing to map or replace in an empty file; any recorded hits are stale by definition.
                lock (gate)
                {
                    skippedStaleCount += hits.Count;
                }

                return;
            }

            using var mmf = MemoryMappedFile.CreateFromFile(
                stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
            using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            var handle = view.SafeMemoryMappedViewHandle;

            byte* basePtr = null;
            try
            {
                handle.AcquirePointer(ref basePtr);
                var input = new RegExPinnedBytes(basePtr + view.PointerOffset, (nuint)length);
                ApplyToMappedFile(path, hits, input);
            }
            finally
            {
                if (basePtr != null)
                {
                    handle.ReleasePointer();
                }
            }
        }

        private void ApplyToMappedFile(string path, List<HitRecord> hits, RegExPinnedBytes input)
        {
            // Collect the valid, non-overlapping edits (offset-ascending). A stale hit is counted and skipped;
            // an edit overlapping the previous accepted one is skipped defensively (matches from one regex
            // pass never overlap, but the records could be malformed).
            var edits = new List<(nuint Begin, nuint End, byte[] Replacement)>(hits.Count);
            var localStale = 0;
            nuint acceptedEnd = 0;
            var haveAccepted = false;

            foreach (var hit in hits)
            {
                if (!ContextStillMatches(input, hit))
                {
                    localStale++;
                    continue;
                }

                var begin = hit.MatchFileOffset;
                var end = begin + (nuint)hit.MatchBytes.Length;

                if (haveAccepted && begin < acceptedEnd)
                {
                    continue; // overlaps a prior accepted edit; skip
                }

                edits.Add((begin, end, hit.ReplacementBytes));
                acceptedEnd = end;
                haveAccepted = true;
            }

            if (edits.Count == 0)
            {
                lock (gate)
                {
                    skippedStaleCount += localStale;
                }

                return; // nothing valid to write; leave the file untouched
            }

            using (var destination = RegEx.CreateReplacementFileStream(path))
            {
                nuint cursor = 0;
                foreach (var edit in edits)
                {
                    WriteRange(destination, input, cursor, edit.Begin);   // unchanged span before the match
                    destination.Write(new ArraySegment<byte>(edit.Replacement)); // the captured replacement
                    cursor = edit.End;
                }

                WriteRange(destination, input, cursor, input.Size);       // tail after the last match

                destination.Flush();
                destination.MoveTo(path, RegExFileMoveFlags.ReplaceExisting);
            }

            lock (gate)
            {
                appliedCount += edits.Count;
                skippedStaleCount += localStale;
                changedFiles.Add(path);
            }
        }

        // Writes input[begin, end) to the destination verbatim (a copy through a managed array; the byte
        // budget here is the unchanged file content). No-op for an empty range.
        private static void WriteRange(RegExFileStream destination, RegExPinnedBytes input, nuint begin, nuint end)
        {
            if (end <= begin)
            {
                return;
            }

            destination.Write(new ArraySegment<byte>(input.Slice(begin, end - begin).ToArray()));
        }

        // Re-verifies that a record's captured pre / match / post bytes still surround its offset in the file
        // being rewritten — the staleness guard. Reproduces the clamped windows CollectingSink captured, so
        // equality holds only if the file is byte-identical there.
        private static bool ContextStillMatches(RegExPinnedBytes input, HitRecord record)
        {
            var matchBegin = record.MatchFileOffset;
            var matchEnd = matchBegin + (nuint)record.MatchBytes.Length;
            var preLength = (nuint)record.PreMatchBytes.Length;
            var postLength = (nuint)record.PostMatchBytes.Length;

            // The captured windows can't extend past what the current file provides.
            if (matchEnd > input.Size || preLength > matchBegin || matchEnd + postLength > input.Size)
            {
                return false;
            }

            return BytesEqual(input, matchBegin - preLength, record.PreMatchBytes)
                && BytesEqual(input, matchBegin, record.MatchBytes)
                && BytesEqual(input, matchEnd, record.PostMatchBytes);
        }

        // Compares expected against the input bytes starting at inputBegin, via the pinned-bytes indexer
        // (no Span on .NET Framework 4.8). The caller has already bounds-checked the range.
        private static bool BytesEqual(RegExPinnedBytes input, nuint inputBegin, byte[] expected)
        {
            for (var i = 0; i < expected.Length; i++)
            {
                if (input[inputBegin + (nuint)i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void SetState(SearchJobState newState)
        {
            state = newState;
            RaiseProgress();
        }

        private void RaiseProgress() => ProgressChanged?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cancellation.Dispose();
        }
    }
}
