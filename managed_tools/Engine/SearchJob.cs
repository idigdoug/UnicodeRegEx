namespace UnicodeRegEx.Tools.Engine
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.MemoryMappedFiles;
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
    /// A single, stateful, one-shot search (or search+replace) over a set of roots. Construct it with a
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
        private int errorCount;
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

        /// <summary>The number of files reported as errors so far (missing roots, access failures, or per-file faults).</summary>
        public int ErrorCount => Volatile.Read(ref errorCount);

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

            var fileFilterList = request.FileNameFilters;
            // grep's rule for filenames: if no filter matches, include unless the first filter is an include.
            var fileDefaultInclude = fileFilterList.Count == 0 || fileFilterList[0].Kind != FilterKind.Include;
            var fileFilters = GlobFilterSet.Compile(fileFilterList, fileDefaultInclude);
            // Directories: an unmatched directory is always included (Option B), so a leading include
            // never prunes everything. Applied uniformly to roots and discovered subdirectories.
            var directoryFilters = GlobFilterSet.Compile(request.DirectoryFilters, defaultIncludeWhenNoMatch: true);
            var files = new List<string>();
            foreach (var path in EnumerateFiles(request.Paths, fileFilters, directoryFilters))
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

            foreach (var path in files)
            {
                if (cancellation.IsCancellationRequested)
                {
                    Finish(SearchJobState.Canceled, new SearchSummary(anyMatch, filesChanged, Volatile.Read(ref errorCount), cancelled: true));
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
                catch (SinkException ex)
                {
                    // A sink callback threw — treat as a bug and fault the whole job, surfacing the
                    // original exception (with its stack) rather than the wrapper.
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                }
                catch (Exception ex)
                {
                    ReportError(path, ex);
                }

                Interlocked.Increment(ref completedFileCount);
                RaiseProgress();
            }

            Finish(SearchJobState.Completed, new SearchSummary(anyMatch, filesChanged, Volatile.Read(ref errorCount), cancelled: false));
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

        // Sink calls run under the gate so a sink need not be thread-safe. A sink that throws is a bug,
        // not a per-file error: wrap the throw as a SinkException so the per-file handler re-throws it
        // (faulting the job) instead of reporting it as a file error.
        private SearchResponse ReportFile(SearchFile file)
        {
            lock (sinkGate)
            {
                try
                {
                    return sink.OnFile(file);
                }
                catch (Exception ex)
                {
                    throw new SinkException(ex);
                }
            }
        }

        private SearchResponse ReportHit(in SearchHit hit)
        {
            lock (sinkGate)
            {
                try
                {
                    return sink.OnHit(hit);
                }
                catch (Exception ex)
                {
                    throw new SinkException(ex);
                }
            }
        }

        private void ReportFileChanged(string path)
        {
            lock (sinkGate)
            {
                try
                {
                    sink.OnFileChanged(path);
                }
                catch (Exception ex)
                {
                    throw new SinkException(ex);
                }
            }
        }

        private void ReportError(string path, Exception exception)
        {
            Interlocked.Increment(ref errorCount);
            lock (sinkGate)
            {
                try
                {
                    sink.OnError(path, exception);
                }
                catch (Exception ex)
                {
                    throw new SinkException(ex);
                }
            }
        }

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

                var detection = EncodingDetector.Detect(
                    new RegExPinnedBytes((void*)data, (nuint)length), length, request.ResolvedDefaultCodePage, request.EncodingDetection);
                if (detection.LooksBinary && request.SkipBinaryFiles)
                {
                    return false;
                }

                var codePage = detection.CodePage;
                var file = new SearchFile(path, codePage, detection.LooksBinary);
                var fileResponse = ReportFile(file);
                if (fileResponse == SearchResponse.StopAll)
                {
                    cancellation.Cancel();
                    return false;
                }

                if (fileResponse == SearchResponse.StopFile)
                {
                    return false;
                }

                var replaceTemplate = request.Verb == SearchVerb.Replace ? request.ReplaceTemplate : null;
                var enumerateOptions = new RegExEnumerateOptions
                {
                    MatchFlags = request.MatchFlags,
                    FormatTemplate = replaceTemplate,
                };

                return regex.EnumerateMatches(
                    new RegExInput(handle, (nuint)view.PointerOffset, (nuint)length, codePage),
                    enumerateOptions,
                    matches =>
                    {
                        var matched = false;
                        foreach (var match in matches)
                        {
                            var hit = new SearchHit(file, match, replaceTemplate != null);
                            var response = ReportHit(hit);
                            matched = true;

                            if (response == SearchResponse.StopAll)
                            {
                                cancellation.Cancel();
                                break;
                            }

                            if (response == SearchResponse.StopFile)
                            {
                                break;
                            }
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

                var detection = EncodingDetector.Detect(
                    new RegExPinnedBytes((void*)data, (nuint)length), length, request.ResolvedDefaultCodePage, request.EncodingDetection);
                if (detection.LooksBinary && request.SkipBinaryFiles)
                {
                    return false;
                }

                var codePage = detection.CodePage;
                var file = new SearchFile(path, codePage, detection.LooksBinary);
                var fileResponse = ReportFile(file);
                if (fileResponse == SearchResponse.StopAll)
                {
                    cancellation.Cancel();
                    return false;
                }

                if (fileResponse == SearchResponse.StopFile)
                {
                    return false;
                }

                var input = new RegExInput(handle, (nuint)view.PointerOffset, (nuint)length, codePage);
                var options = new RegExEnumerateOptions
                {
                    MatchFlags = request.MatchFlags,
                    FormatTemplate = request.ReplaceTemplate,
                };

                // Write into a delete-on-close temp adjacent to the file; commit with MoveTo on success.
                using var destination = RegEx.CreateReplacementFileStream(path);
                var stopped = false;
                var matched = regex.EnumerateSegments(input, options, segments =>
                {
                    var any = false;
                    foreach (var segment in segments)
                    {
                        if (segment.IsMatch)
                        {
                            // Report the match before writing it, so a StopFile/StopAll response abandons
                            // the rewrite: we leave the loop without committing, the delete-on-close temp
                            // is discarded, and the original file is left untouched.
                            var response = ReportHit(new SearchHit(file, segment.Match, isReplace: true));
                            if (response == SearchResponse.StopAll)
                            {
                                cancellation.Cancel();
                                stopped = true;
                                break;
                            }

                            if (response == SearchResponse.StopFile)
                            {
                                stopped = true;
                                break;
                            }

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

                if (stopped || !matched)
                {
                    return false;
                }

                destination.Flush();
                destination.MoveTo(path, RegExFileMoveFlags.ReplaceExisting);

                ReportFileChanged(path);

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

        private IEnumerable<string> EnumerateFiles(IEnumerable<string> roots, GlobFilterSet? fileFilters, GlobFilterSet? directoryFilters)
        {
            // Directories (roots and discovered subdirectories alike) run the same rule: the directory
            // filter decides whether the directory is considered at all, then the disposition decides
            // what to do with it. Error/Skip never enumerate contents and Recurse* is the only disposition
            // that pushes subdirectories, so those two can only ever apply to a root -- a directory popped
            // from the stack is always a Read/Recurse case. The disposition is fixed for the whole run, so
            // the recurse decision is computed once here rather than per popped directory.
            var recurse = request.Directories == DirectoryDisposition.RecurseNoLinks ||
                request.Directories == DirectoryDisposition.RecurseWithLinks;
            var followLinks = request.Directories == DirectoryDisposition.RecurseWithLinks;

            // The stack holds DirectoryInfo objects so that each discovered subdirectory reuses the one
            // the enumeration already produced (no fresh DirectoryInfo per directory). NOTE: a popped
            // DirectoryInfo's own cached attributes are enumeration-time snapshots and are NOT relied on
            // here -- we always re-enumerate it, and the reparse-point check reads each *child's*
            // attributes, so staleness never matters.
            var stack = new Stack<DirectoryInfo>();
            foreach (var root in roots)
            {
                FileAttributes attributes;
                try
                {
                    // One metadata probe instead of File.Exists + Directory.Exists.
                    attributes = File.GetAttributes(root);
                }
                catch (Exception ex)
                {
                    ReportError(root, ex);
                    continue;
                }

                if ((attributes & FileAttributes.Directory) == 0)
                {
                    // An explicitly named file is always searched, bypassing the file-name filters.
                    yield return root;
                    continue;
                }

                if (!DirectoryIsIncluded(directoryFilters, Path.GetFileName(root)))
                {
                    continue;
                }

                // Error/Skip apply only to a directory given as a search target; they never recurse, so
                // they are handled here and the walk loop below is entered only for Read/Recurse roots.
                if (request.Directories == DirectoryDisposition.Error)
                {
                    ReportError(root, new IOException("Is a directory"));
                    continue;
                }

                if (request.Directories == DirectoryDisposition.Skip)
                {
                    continue;
                }

                stack.Push(new DirectoryInfo(root));
                while (stack.Count > 0)
                {
                    if (cancellation.IsCancellationRequested)
                    {
                        yield break;
                    }

                    var current = stack.Pop();

                    // A single streaming enumeration: each FileSystemInfo already carries the attributes
                    // from the underlying directory scan (no second scan, and no per-subdirectory
                    // GetAttributes for the reparse-point check). When not recursing, EnumerateFiles skips
                    // materializing subdirectories entirely (they would only be discarded). MoveNext can
                    // throw mid-stream (an entry vanished, access denied), so it runs under a try; a
                    // failure reports the directory and moves on to the next stacked directory, matching
                    // the whole-directory catch the eager GetFiles/GetDirectories form had.
                    IEnumerator<FileSystemInfo> entries;
                    try
                    {
                        entries = recurse
                            ? current.EnumerateFileSystemInfos().GetEnumerator()
                            : ((IEnumerable<FileSystemInfo>)current.EnumerateFiles()).GetEnumerator();
                    }
                    catch (Exception ex)
                    {
                        ReportError(current.FullName, ex);
                        continue;
                    }

                    try
                    {
                        while (true)
                        {
                            FileSystemInfo entry;
                            try
                            {
                                if (!entries.MoveNext())
                                {
                                    break;
                                }

                                entry = entries.Current;
                            }
                            catch (Exception ex)
                            {
                                ReportError(current.FullName, ex);
                                break;
                            }

                            // Directory entries only arrive from the recursing (EnumerateFileSystemInfos)
                            // path, so reaching this branch already implies recursion.
                            if (entry is DirectoryInfo subdirectory)
                            {
                                if (DirectoryIsIncluded(directoryFilters, subdirectory.Name) &&
                                    (followLinks || (subdirectory.Attributes & FileAttributes.ReparsePoint) == 0))
                                {
                                    stack.Push(subdirectory);
                                }
                            }
                            else if (fileFilters == null || fileFilters.ShouldInclude(entry.Name))
                            {
                                yield return entry.FullName;
                            }
                        }
                    }
                    finally
                    {
                        entries.Dispose();
                    }
                }
            }
        }

        private static bool DirectoryIsIncluded(GlobFilterSet? directoryFilters, string name) =>
            directoryFilters == null || directoryFilters.ShouldInclude(name);

        // Marks an exception as having come from a sink callback (a bug) rather than from file/IO work,
        // so the per-file handler re-throws it to fault the job instead of reporting it as a file error.
        private sealed class SinkException : Exception
        {
            public SinkException(Exception inner)
                : base("A search sink callback threw.", inner)
            {
            }
        }
    }
}
