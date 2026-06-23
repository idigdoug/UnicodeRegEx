namespace UnicodeRegEx.Tools.Engine
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.MemoryMappedFiles;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using UnicodeRegEx;

    /// <summary>The lifecycle phase of a <see cref="SearchJob"/>.</summary>
    public enum SearchJobState
    {
        /// <summary>Constructed but not started.</summary>
        Created,

        /// <summary>Building the list of files to process; <see cref="SearchJob.TotalFileCount"/> is still growing.</summary>
        Enumerating,

        /// <summary>Processing files; <see cref="SearchJob.TotalFileCount"/> is final and <see cref="SearchJob.CompletedFileCount"/> is growing.</summary>
        Processing,

        /// <summary>Finished normally.</summary>
        Completed,

        /// <summary>Stopped early because <see cref="SearchJob.Cancel"/> was requested.</summary>
        Canceled,

        /// <summary>Stopped because of an unhandled error (e.g. an invalid pattern); see the task returned by <see cref="SearchJob.RunAsync"/>.</summary>
        Faulted,
    }

    /// <summary>
    /// A single, stateful, one-shot search (or search+replace) over a set of paths. Construct it with a
    /// <see cref="SearchRequest"/> (which it snapshots), call <see cref="RunAsync"/> once, observe
    /// progress via <see cref="State"/>/<see cref="TotalFileCount"/>/<see cref="CompletedFileCount"/>
    /// and the <see cref="ProgressChanged"/> event, and stop it early with <see cref="Cancel"/>.
    /// Results stream through the <see cref="ISearchSink"/> passed to <see cref="RunAsync"/>.
    /// </summary>
    /// <remarks>
    /// Front-end-neutral and intended to move into the shared core. This first version runs on a single
    /// background thread in two passes — enumerate the full file list (so progress is determinate), then
    /// process it — and is structured so multi-threaded enumeration/processing can be added later without
    /// changing this surface. <see cref="ISearchSink"/> calls are serialized by the job, so a sink need
    /// not be thread-safe. <see cref="ProgressChanged"/> and the sink callbacks are raised on the job's
    /// background thread; a UI consumer is responsible for marshaling to its own thread. The event is a
    /// "something changed, read the current values" signal (no payload), so it can be coalesced later.
    /// Owns a <see cref="CancellationTokenSource"/>, so it is <see cref="IDisposable"/>; dispose it once
    /// the run has completed (the usual <c>using</c> + <c>await RunAsync</c> pattern). Disposing while a
    /// run is still in flight is a misuse — call <see cref="Cancel"/> and await the run first.
    /// </remarks>
    public sealed class SearchJob : IDisposable
    {
        private readonly SearchRequest request;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly object sinkGate = new object();
        private readonly ISearchSink sink;

        private int totalFileCount;
        private int completedFileCount;
        private volatile SearchJobState state = SearchJobState.Created;
        private int started;
        private bool disposed;

        public SearchJob(SearchRequest request, ISearchSink sink)
        {
            this.request = request.Clone();
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>The job's lifecycle phase.</summary>
        public SearchJobState State => state;

        /// <summary>
        /// The number of files to process. Grows while <see cref="State"/> is
        /// <see cref="SearchJobState.Enumerating"/>, then is final for the rest of the run.
        /// </summary>
        public int TotalFileCount => Volatile.Read(ref totalFileCount);

        /// <summary>The number of files processed so far. Grows while <see cref="State"/> is <see cref="SearchJobState.Processing"/>.</summary>
        public int CompletedFileCount => Volatile.Read(ref completedFileCount);

        /// <summary>The aggregate outcome. Meaningful once the job reaches a terminal state.</summary>
        public SearchSummary Summary { get; private set; }

        /// <summary>The job's cancellation token, signaled by <see cref="Cancel"/>.</summary>
        public CancellationToken CancellationToken => cancellation.Token;

        /// <summary>
        /// Raised when <see cref="State"/>, <see cref="TotalFileCount"/>, or <see cref="CompletedFileCount"/>
        /// changes. Carries no data — read the properties. Raised on the job's background thread.
        /// </summary>
        public event EventHandler? ProgressChanged;

        /// <summary>Requests cancellation; the job stops at the next file boundary and ends in <see cref="SearchJobState.Canceled"/>.</summary>
        public void Cancel() => cancellation.Cancel();

        /// <summary>
        /// Releases the resources owned by this job (notably its <see cref="CancellationTokenSource"/>).
        /// Call this only after the run has finished; disposing during an in-flight run is a misuse (see
        /// the type remarks). Safe to call more than once.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cancellation.Dispose();
        }

        /// <summary>
        /// Starts the run on a background thread and returns a task that completes when the job reaches a
        /// terminal state. May be called only once. An invalid pattern faults the returned task (and sets
        /// <see cref="SearchJobState.Faulted"/>); per-file errors are reported through the sink, not thrown.
        /// </summary>
        public Task RunAsync()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SearchJob));
            }

            if (Interlocked.Exchange(ref started, 1) != 0)
            {
                throw new InvalidOperationException("A SearchJob can only be run once.");
            }

            return Task.Run(() => Run());
        }

        private void Run()
        {
            try
            {
                var syntaxFlags = request.SyntaxFlags;
                if (request.IgnoreCase)
                {
                    syntaxFlags |= RegExSyntaxFlags.ICase;
                }

                // An invalid pattern is a setup failure for the whole run; it faults the task.
                using var regex = RegEx.Create(request.Pattern, syntaxFlags);

                var files = Enumerate();
                if (cancellation.IsCancellationRequested)
                {
                    Finish(SearchJobState.Canceled, new SearchSummary(false, 0, 0, cancelled: true));
                    return;
                }

                Process(regex, files);
            }
            catch (Exception)
            {
                SetState(SearchJobState.Faulted);
                throw;
            }
        }

        // PHASE 1: build the complete file list so TotalFileCount is known before processing begins.
        private List<string> Enumerate()
        {
            SetState(SearchJobState.Enumerating);

            var include = GlobToRegex.Compile(request.Include);
            var files = new List<string>();
            foreach (var path in EnumerateFiles(request.Paths, include))
            {
                if (cancellation.IsCancellationRequested)
                {
                    break;
                }

                files.Add(path);
                Volatile.Write(ref totalFileCount, files.Count);
                RaiseProgress();
            }

            return files;
        }

        // PHASE 2: process each file, reporting results through the sink and ticking progress.
        private void Process(RegEx regex, List<string> files)
        {
            SetState(SearchJobState.Processing);

            var anyMatch = false;
            var filesChanged = 0;
            var errors = 0;

            foreach (var path in files)
            {
                if (cancellation.IsCancellationRequested)
                {
                    Finish(SearchJobState.Canceled, new SearchSummary(anyMatch, filesChanged, errors, cancelled: true));
                    return;
                }

                try
                {
                    if (request.Apply)
                    {
                        if (ApplyReplaceFile(regex, path))
                        {
                            filesChanged++;
                            anyMatch = true;
                        }
                    }
                    else if (MatchFile(regex, path))
                    {
                        anyMatch = true;
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    lock (sinkGate)
                    {
                        sink.OnError(path, ex.Message);
                    }
                }

                Interlocked.Increment(ref completedFileCount);
                RaiseProgress();
            }

            Finish(SearchJobState.Completed, new SearchSummary(anyMatch, filesChanged, errors, cancelled: false));
        }

        private void Finish(SearchJobState terminal, SearchSummary summary)
        {
            Summary = summary;
            SetState(terminal);
        }

        private void SetState(SearchJobState newState)
        {
            state = newState;
            RaiseProgress();
        }

        private void RaiseProgress() => ProgressChanged?.Invoke(this, EventArgs.Empty);

        private unsafe bool MatchFile(RegEx regex, string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            if (length == 0)
            {
                return false;
            }

            using var mmf = MemoryMappedFile.CreateFromFile(
                stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
            using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            var handle = view.SafeMemoryMappedViewHandle;

            byte* basePtr = null;
            try
            {
                handle.AcquirePointer(ref basePtr);
                var data = basePtr + view.PointerOffset;

                var codePage = DetectCodePage(data, length, request.ResolvedDefaultCodePage, out _);
                if (codePage != RegExCodePage.Utf16LE &&
                    codePage != RegExCodePage.Utf16BE &&
                    LooksBinary(data, length))
                {
                    return false;
                }

                var replaceTemplate = request.Verb == SearchVerb.Replace ? request.ReplaceTemplate : null;
                var enumerateOptions = replaceTemplate == null
                    ? default
                    : new RegExEnumerateOptions { FormatTemplate = replaceTemplate };

                return regex.EnumerateMatches(
                    new RegExInput(handle, (nuint)view.PointerOffset, (nuint)length, codePage),
                    enumerateOptions,
                    matches =>
                    {
                        var matched = false;
                        foreach (var match in matches)
                        {
                            var replacement = replaceTemplate != null ? match.Format() : null;
                            var hit = new SearchHit(path, match.Text, replacement);
                            lock (sinkGate)
                            {
                                sink.OnHit(hit);
                            }

                            matched = true;
                        }

                        return matched;
                    });
            }
            finally
            {
                if (basePtr != null)
                {
                    handle.ReleasePointer();
                }
            }
        }

        // Streams the file through the regex segment-by-segment into a replacement file: unmatched
        // runs are copied verbatim and matches are written as their formatted replacement, all in the
        // file's detected code page (no intermediate string, no added BOM). The result is committed
        // atomically only if at least one match was found; otherwise the temporary file is abandoned
        // (it is delete-on-close) and the original is left untouched.
        private unsafe bool ApplyReplaceFile(RegEx regex, string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = stream.Length;
            if (length == 0)
            {
                return false;
            }

            using var mmf = MemoryMappedFile.CreateFromFile(
                stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: true);
            using var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            var handle = view.SafeMemoryMappedViewHandle;

            byte* basePtr = null;
            try
            {
                handle.AcquirePointer(ref basePtr);
                var data = basePtr + view.PointerOffset;

                var codePage = DetectCodePage(data, length, request.ResolvedDefaultCodePage, out _);
                if (codePage != RegExCodePage.Utf16LE &&
                    codePage != RegExCodePage.Utf16BE &&
                    LooksBinary(data, length))
                {
                    return false;
                }

                var input = new RegExInput(handle, (nuint)view.PointerOffset, (nuint)length, codePage);
                var options = new RegExEnumerateOptions { FormatTemplate = request.ReplaceTemplate };

                // Write into a delete-on-close temp adjacent to the file; commit with MoveTo on success.
                using var destination = RegEx.CreateReplacementFileStream(path);
                var matched = regex.EnumerateSegments(input, options, segments =>
                {
                    var any = false;
                    foreach (var segment in segments)
                    {
                        if (segment.IsMatch)
                        {
                            segment.Match.FormatTo(destination, codePage);
                            any = true;
                        }
                        else
                        {
                            segment.CopyTo(destination, codePage);
                        }
                    }

                    return any;
                });

                if (!matched)
                {
                    return false;
                }

                destination.Flush();
                destination.MoveTo(path, RegExFileMoveFlags.ReplaceExisting);

                lock (sinkGate)
                {
                    sink.OnFileChanged(path);
                }

                return true;
            }
            finally
            {
                if (basePtr != null)
                {
                    handle.ReleasePointer();
                }
            }
        }

        private IEnumerable<string> EnumerateFiles(IEnumerable<string> paths, Regex? include)
        {
            foreach (var path in paths)
            {
                FileAttributes attributes;
                try
                {
                    // One metadata probe instead of File.Exists + Directory.Exists.
                    attributes = File.GetAttributes(path);
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                {
                    lock (sinkGate)
                    {
                        sink.OnError(path, "no such file or directory");
                    }
                    continue;
                }
                catch (Exception ex)
                {
                    lock (sinkGate)
                    {
                        sink.OnError(path, ex.Message);
                    }
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    foreach (var file in EnumerateDirectory(path, include))
                    {
                        yield return file;
                    }
                }
                else
                {
                    // An explicitly named file is always searched, bypassing the include filter.
                    yield return path;
                }
            }
        }

        private IEnumerable<string> EnumerateDirectory(string root, Regex? include)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                if (cancellation.IsCancellationRequested)
                {
                    yield break;
                }

                var current = stack.Pop();
                string[] files;
                string[] subdirectories;
                try
                {
                    files = Directory.GetFiles(current);
                    subdirectories = request.Recurse ? Directory.GetDirectories(current) : Array.Empty<string>();
                }
                catch (Exception ex)
                {
                    lock (sinkGate)
                    {
                        sink.OnError(current, ex.Message);
                    }
                    continue;
                }

                foreach (var file in files)
                {
                    if (include == null || include.IsMatch(Path.GetFileName(file)))
                    {
                        yield return file;
                    }
                }

                foreach (var subdirectory in subdirectories)
                {
                    stack.Push(subdirectory);
                }
            }
        }

        private static unsafe int DetectCodePage(byte* data, long length, int defaultCodePage, out int bomLength)
        {
            if (length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                bomLength = 3;
                return RegExCodePage.Utf8;
            }

            if (length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            {
                bomLength = 2;
                return RegExCodePage.Utf16LE;
            }

            if (length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            {
                bomLength = 2;
                return RegExCodePage.Utf16BE;
            }

            bomLength = 0;
            return defaultCodePage;
        }

        private static unsafe bool LooksBinary(byte* data, long length)
        {
            var n = (int)Math.Min(length, 8000);
            for (var i = 0; i < n; i++)
            {
                if (data[i] == 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
