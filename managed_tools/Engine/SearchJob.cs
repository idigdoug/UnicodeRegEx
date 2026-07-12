namespace UnicodeRegEx.Tools.Engine
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.MemoryMappedFiles;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Win32.SafeHandles;
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
    /// Front-end-neutral and intended to move into the shared core. It runs on a background thread in two
    /// passes — enumerate the full file list (so progress is determinate), then process it. Processing is
    /// serial by default and can be parallelized across files via
    /// <see cref="SearchRequest.MaxDegreeOfParallelism"/>.
    /// <para>
    /// THREADING: the job does <b>not</b> serialize <see cref="ISearchSink"/> callbacks. A single file's
    /// callbacks (<see cref="ISearchSink.OnFile"/> → its <see cref="ISearchSink.OnHit"/>s →
    /// <see cref="ISearchSink.OnFileComplete"/>) always run on one thread, in order, because one file is
    /// processed by one worker — so a sink needs no synchronization for state scoped to a single file (use
    /// <see cref="SearchFile.Context"/> to carry it). But under parallel processing DIFFERENT files run on
    /// different threads concurrently, so a sink <b>is</b> responsible for the thread-safety of any state it
    /// shares across files (counters, output streams, collections). At
    /// <see cref="SearchRequest.MaxDegreeOfParallelism"/> = 1 (the default) everything runs on one thread and
    /// no synchronization is needed at all.
    /// </para>
    /// <see cref="ProgressChanged"/> and the sink callbacks are raised on the job's worker thread(s); a UI
    /// consumer is responsible for marshaling to its own thread. The event is a "something changed, read the
    /// current values" signal (no payload), so it can be coalesced. Owns a
    /// <see cref="CancellationTokenSource"/>, so it is <see cref="IDisposable"/>; dispose it once the run
    /// has completed (the usual <c>using</c> + <c>await RunAsync</c> pattern). Disposing while a run is
    /// still in flight is a misuse — call <see cref="Cancel"/> and await the run first.
    /// </remarks>
    public sealed class SearchJob : IDisposable
    {
        private readonly SearchRequest request;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly ISearchSink sink;

        private int totalFileCount;
        private int completedFileCount;
        private int errorCount;
        // Accumulated across (possibly parallel) file processing. anyMatchFlag is 0/1; filesChangedCount
        // counts rewritten files. Both are updated with interlocked ops so the parallel path is safe.
        private int anyMatchFlag;
        private int filesChangedCount;
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

                // Compile the pattern for the run. A front-end can call SearchRequest.Validate() first to
                // catch an invalid pattern (and the other request problems) up front; if it does not, an
                // invalid pattern is a setup failure that faults the task here. Disposed at the end of the run.
                using var regex = RegEx.Create(request.Pattern, syntaxFlags, request.Lcid);

                var files = Enumerate();
                if (cancellation.IsCancellationRequested)
                {
                    Finish(SearchJobState.Canceled, new SearchSummary(false, 0, 0, cancelled: true));
                    return;
                }

                Process(regex, files);
            }
            catch (SinkException ex)
            {
                // A sink callback threw. The job faults with the ORIGINAL exception (with its stack),
                // not the internal SinkException wrapper. Both the serial and parallel processing paths
                // funnel here, so unwrapping in one place keeps their behavior identical.
                SetState(SearchJobState.Faulted);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
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

        // PHASE 2: process each file, reporting results through the sink and ticking progress. Files are
        // processed serially when the resolved degree of parallelism is 1 (the default -- deterministic
        // ordering, no parallel overhead), or concurrently otherwise. The compiled regex is shared across
        // workers (it is immutable and free-threaded), and every file's work is independent (its own file
        // handle, memory map, and match enumerator). Sink callbacks are NOT serialized by the job: a single
        // file's callbacks run on one worker thread in order, but different files run concurrently, so the
        // sink owns the thread-safety of any cross-file state. The engine's own shared state (the cancellation
        // source and the counters) is thread-safe (interlocked).
        private void Process(RegEx regex, List<string> files)
        {
            SetState(SearchJobState.Processing);

            var dop = ResolveDegreeOfParallelism();

            if (dop == 1)
            {
                foreach (var path in files)
                {
                    if (cancellation.IsCancellationRequested)
                    {
                        Finish(SearchJobState.Canceled, BuildSummary(cancelled: true));
                        return;
                    }

                    ProcessOne(regex, path);
                }

                Finish(SearchJobState.Completed, BuildSummary(cancelled: false));
                return;
            }

            // Parallel path. The cancellation token stops in-flight workers promptly; a StopAll response or
            // an explicit Cancel trips it. A sink that throws is a bug: ProcessOne wraps it as a
            // SinkException, which Parallel.ForEach surfaces inside an AggregateException -- unwrap it back to
            // the SinkException and rethrow so Run's catch faults the job with the original exception, exactly
            // as the serial path does.
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = dop,
                CancellationToken = cancellation.Token,
            };

            try
            {
                Parallel.ForEach(files, options, path => ProcessOne(regex, path));
            }
            catch (OperationCanceledException)
            {
                Finish(SearchJobState.Canceled, BuildSummary(cancelled: true));
                return;
            }
            catch (AggregateException ex)
            {
                var sinkException = FindSinkException(ex);
                if (sinkException != null)
                {
                    throw sinkException;
                }

                throw;
            }

            // A StopAll response cancels via the token without throwing (Parallel.ForEach only throws
            // OperationCanceledException when the token was passed and a body observes it after the loop
            // decides to stop); reflect the cancellation in the terminal state.
            if (cancellation.IsCancellationRequested)
            {
                Finish(SearchJobState.Canceled, BuildSummary(cancelled: true));
                return;
            }

            Finish(SearchJobState.Completed, BuildSummary(cancelled: false));
        }

        // Processes a single file: runs the verb, updates the shared match/changed counters, reports
        // per-file errors, and ticks progress. Safe to call from multiple threads concurrently. A sink
        // callback that throws is surfaced as a SinkException (a bug that faults the whole job); genuine
        // per-file IO failures are reported via OnError and do not stop other files.
        private void ProcessOne(RegEx regex, string path)
        {
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (request.Verb == SearchVerb.Apply)
                {
                    if (ApplyReplaceFile(regex, path))
                    {
                        Interlocked.Increment(ref filesChangedCount);
                        Volatile.Write(ref anyMatchFlag, 1);
                    }
                }
                else if (MatchFile(regex, path))
                {
                    Volatile.Write(ref anyMatchFlag, 1);
                }
            }
            catch (SinkException)
            {
                // A sink callback threw. Let it propagate: on the serial path it faults the job directly;
                // on the parallel path Parallel.ForEach collects it into an AggregateException, which
                // Process unwraps back to the original.
                throw;
            }
            catch (Exception ex)
            {
                ReportError(path, ex);
            }

            Interlocked.Increment(ref completedFileCount);
            RaiseProgress();
        }

        // Resolves the request's MaxDegreeOfParallelism: 0 => automatic (ProcessorCount), otherwise the
        // requested value clamped to at least 1.
        private int ResolveDegreeOfParallelism()
        {
            var requested = request.MaxDegreeOfParallelism;
            if (requested == 0)
            {
                return Math.Max(1, Environment.ProcessorCount);
            }

            return Math.Max(1, requested);
        }

        private SearchSummary BuildSummary(bool cancelled) => new SearchSummary(
            Volatile.Read(ref anyMatchFlag) != 0,
            Volatile.Read(ref filesChangedCount),
            Volatile.Read(ref errorCount),
            cancelled);

        // Finds the first SinkException within an AggregateException's flattened inner exceptions (the
        // parallel path collects worker exceptions here). Returns null if none is a SinkException.
        private static SinkException? FindSinkException(AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                if (inner is SinkException sinkException)
                {
                    return sinkException;
                }
            }

            return null;
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

        // Invokes the sink for a file. Callbacks are NOT serialized across files: a file's own callbacks
        // all run on one thread (one file = one processing iteration), but different files may run
        // concurrently, so a sink owns the thread-safety of any state it shares across files. A sink that
        // throws is a bug, not a per-file error: wrap the throw as a SinkException so the per-file handler
        // re-throws it (faulting the job) instead of reporting it as a file error.
        private SearchResponse ReportFile(SearchFile file, RegExPinnedBytes bytes)
        {
            try
            {
                return sink.OnFile(file, bytes);
            }
            catch (Exception ex)
            {
                throw new SinkException(ex);
            }
        }

        private SearchResponse ReportMatch(in SearchHit hit)
        {
            try
            {
                return sink.OnMatch(hit);
            }
            catch (Exception ex)
            {
                throw new SinkException(ex);
            }
        }

        private ApplyAction ReportApply(in SearchHit hit)
        {
            try
            {
                return sink.OnApply(hit);
            }
            catch (Exception ex)
            {
                throw new SinkException(ex);
            }
        }

        private void ReportFileChanged(string path)
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

        private void ReportFileComplete(SearchFile file)
        {
            try
            {
                sink.OnFileComplete(file);
            }
            catch (Exception ex)
            {
                throw new SinkException(ex);
            }
        }

        private void ReportError(string path, Exception exception)
        {
            Interlocked.Increment(ref errorCount);
            try
            {
                sink.OnError(path, exception);
            }
            catch (Exception ex)
            {
                throw new SinkException(ex);
            }
        }

        // The verb-specific body invoked by ProcessFile once the input is ready. A named delegate is
        // required because RegExInput is a ref struct and so cannot be a Func<> type argument.
        private delegate bool FileProcessor(RegExInput input, SearchFile file);

        // Shared file handling for both verbs: opens the file, rejects anything that is not a regular
        // on-disk file (device / pipe / socket -> reported as an error, never a silent zero-length
        // "success"), runs encoding/binary detection, reports OnFile, and hands a ready RegExInput to
        // 'process'. A zero-length file is handled with an empty (null pointer / zero length) input
        // rather than a memory map, since an empty file cannot be mapped -- this lets zero-length
        // patterns match and empty replacements run. Returns process's result, or false when the file is
        // skipped (binary, or an OnFile Stop response). Genuine failures throw and are reported by the
        // per-file handler in Process.
        private unsafe bool ProcessFile(string path, FileProcessor process)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            // Only regular on-disk files are searchable: the engine memory-maps a real file. A device,
            // pipe, or socket is reported as an error rather than silently treated as an empty file.
            if (NativeMethods.GetFileType(stream.SafeFileHandle) != NativeMethods.FILE_TYPE_DISK)
            {
                throw new IOException("Not a regular file");
            }

            var length = stream.Length;

            if (length == 0)
            {
                // An empty file cannot be memory-mapped; feed the regex a null / zero-length input (the
                // native layer accepts it). Detection over the zero-length descriptor is safe -- it is
                // never dereferenced at size 0.
                return ProcessFileImpl(path, process, new RegExPinnedBytes());
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
                return ProcessFileImpl(path, process, new RegExPinnedBytes(data, (nuint)length));
            }
            finally
            {
                if (basePtr != null)
                {
                    handle.ReleasePointer();
                }
            }
        }

        // Detection + OnFile + the verb body, shared by the empty and memory-mapped paths.
        bool ProcessFileImpl(string path, FileProcessor process, RegExPinnedBytes bytes)
        {
            var detection = EncodingDetector.Detect(bytes, request.ResolvedDefaultCodePage, request.EncodingDetection);
            if (detection.LooksBinary && request.SkipBinaryFiles)
            {
                return false;
            }

            var file = new SearchFile(path, detection.CodePage, detection.LooksBinary);
            var fileResponse = ReportFile(file, bytes);

            // OnFile has returned: the code page is now committed (a sink could have changed it via
            // OverrideCodePage during the call). Lock it so any later OverrideCodePage throws.
            file.Lock();

            if (fileResponse == SearchResponse.StopAll)
            {
                cancellation.Cancel();
                return false;
            }

            if (fileResponse == SearchResponse.StopFile)
            {
                return false;
            }

            // OnFile returned Continue and the verb body runs to completion here, decoding with the file's
            // (possibly overridden) code page. OnFileComplete is the matching close of the OnFile bracket:
            // it fires only for a file OnFile accepted, and only on normal completion (if the verb body
            // throws, the file is reported via OnError instead, so OnFileComplete does not fire for a
            // faulted file).
            var result = process(new RegExInput(bytes, file.CodePage), file);
            ReportFileComplete(file);
            return result;
        }

        private bool MatchFile(RegEx regex, string path)
        {
            return ProcessFile(path, (input, file) =>
            {
                // The Match verb reports matches, with each match's replacement available as a preview via
                // SearchHit.Replacement. The template is always applied (empty by default -- an empty
                // template previews an empty replacement); there is nothing special about an empty template.
                var enumerateOptions = new RegExEnumerateOptions
                {
                    MatchFlags = request.MatchFlags,
                    FormatFlags = request.FormatFlags,
                    FormatTemplate = request.ReplaceTemplate,
                };

                return regex.EnumerateMatches(input, enumerateOptions, matches =>
                {
                    // When line tracking is on, advance one forward line/column cursor across this file's
                    // matches (they arrive in ascending offset order). AdvanceTo(match position) always
                    // yields a 1-based line/column.
                    var track = request.TrackLineNumbers;
                    var counter = new RegExLineCounter(matches.Input, matches.InputCodePage);

                    var matched = false;
                    foreach (var match in matches)
                    {
                        nuint line = 0;
                        nuint column = 0;
                        if (track)
                        {
                            counter.AdvanceTo(match.GetSubMatch(0).Begin);
                            line = counter.LineNumber;
                            column = counter.Column;
                        }

                        var hit = new SearchHit(file, match, line, column);
                        var response = ReportMatch(hit);
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
            });
        }

        // Streams the file through the regex segment-by-segment into a replacement file: unmatched
        // runs are copied verbatim and matches are written as their formatted replacement, all in the
        // file's detected code page (no intermediate string, no added BOM). The result is committed
        // atomically only if at least one match was found; otherwise the temporary file is abandoned
        // (it is delete-on-close) and the original is left untouched.
        private bool ApplyReplaceFile(RegEx regex, string path)
        {
            return ProcessFile(path, (input, file) =>
            {
                var options = new RegExEnumerateOptions
                {
                    MatchFlags = request.MatchFlags,
                    FormatFlags = request.FormatFlags,
                    FormatTemplate = request.ReplaceTemplate,
                };

                // Write into a delete-on-close temp adjacent to the file; commit with MoveTo on success.
                using var destination = RegEx.CreateReplacementFileStream(path);
                var stopped = false;
                var track = request.TrackLineNumbers;
                var matched = regex.EnumerateSegments(input, options, segments =>
                {
                    // Line/column cursor for this file (over the same pinned input the segments index into);
                    // ref-struct locals must live inside this lambda.
                    var counter = new RegExLineCounter(segments.Input, segments.InputCodePage);

                    var any = false;
                    foreach (var segment in segments)
                    {
                        if (segment.IsMatch)
                        {
                            nuint line = 0;
                            nuint column = 0;
                            if (track)
                            {
                                counter.AdvanceTo(segment.Match.GetSubMatch(0).Begin);
                                line = counter.LineNumber;
                                column = counter.Column;
                            }

                            // Ask the sink what to write for this match before writing anything, so a
                            // StopFile/StopAll abandons the rewrite: we leave the loop without committing,
                            // the delete-on-close temp is discarded, and the original file is left untouched.
                            var action = ReportApply(new SearchHit(file, segment.Match, line, column));
                            switch (action.Kind)
                            {
                                case ApplyActionKind.StopFile:
                                    stopped = true;
                                    break;

                                case ApplyActionKind.StopAll:
                                    cancellation.Cancel();
                                    stopped = true;
                                    break;

                                case ApplyActionKind.Default:
                                    segment.Match.FormatTo(destination, file.CodePage);
                                    break;

                                case ApplyActionKind.Original:
                                    segment.CopyTo(destination, file.CodePage);
                                    break;

                                case ApplyActionKind.Delete:
                                    // Write nothing for this match.
                                    break;

                                case ApplyActionKind.Custom:
                                    // Caller-supplied bytes, written verbatim (the caller owns the encoding).
                                    destination.Write(action.CustomBytes);
                                    break;
                            }

                            if (stopped)
                            {
                                break;
                            }

                            any = true;
                        }
                        else
                        {
                            segment.CopyTo(destination, file.CodePage);
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
            });
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

            // The stack holds directory paths. Enumeration is done with NativeDir (a FindFirstFileEx
            // wrapper), which yields lightweight NativeDirEntry values carrying each child's name, attributes,
            // and reparse tag straight from the directory scan -- no FileSystemInfo objects, and the reparse
            // *tag* (which the BCL does not surface) lets the walk tell real links from non-link reparse
            // points. Each child's path is composed from the popped directory path on demand.
            var stack = new Stack<string>();

            // Cycle prevention (only when following links -- RecurseNoLinks can't loop, and non-recursing
            // dispositions don't descend). We record each descended directory's durable identity (volume
            // serial + 128-bit file id); a directory whose identity was already seen is not descended again.
            // This is a global visited-set, chosen deliberately: it breaks link cycles AND de-duplicates a
            // directory reached by more than one link (a diamond), so the same directory's contents are never
            // searched twice -- the behavior a search tool wants. (The alternative, ancestor-only tracking,
            // would follow non-cycle re-visits and report the same file's matches multiple times.) Off (null)
            // for every non-following disposition, so those paths pay nothing.
            var visited = followLinks ? new HashSet<DirectoryId>() : null;

            // Decides whether to descend into a directory, applying cycle prevention when following links.
            // Returns true if the directory should be walked. When not following links this is a pass-through
            // (no identity probe). When following links: probe the identity; if it was already seen, skip
            // silently (a cycle or an already-searched directory); if the probe fails, skip and report (a
            // followed directory we cannot identify is not descended, so an unresolvable link can't hang it).
            bool TryEnterDirectory(string path)
            {
                if (visited == null)
                {
                    return true;
                }

                if (!NativeDir.TryGetDirectoryId(path, out var id))
                {
                    ReportError(path, new IOException("Could not determine directory identity for cycle detection."));
                    return false;
                }

                // Add returns false if the id was already present -- a cycle or an already-visited directory.
                return visited.Add(id);
            }

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

                // Seed the root's identity so a link back to it (the classic self-referential cycle) is
                // caught. Unlike a discovered subdirectory, a root is walked even if its identity can't be
                // read -- the user named it explicitly -- so this records opportunistically and never skips.
                if (visited != null && NativeDir.TryGetDirectoryId(root, out var rootId))
                {
                    visited.Add(rootId);
                }

                stack.Push(root);
                while (stack.Count > 0)
                {
                    if (cancellation.IsCancellationRequested)
                    {
                        yield break;
                    }

                    var current = stack.Pop();

                    // A single streaming enumeration per directory. NativeDirEntry carries the attributes and
                    // reparse tag from the underlying scan (no second probe per child). When not recursing,
                    // skipDirectories short-circuits directory entries before their name string is even
                    // materialized (matching the BCL EnumerateFiles fast path).
                    //
                    // NativeDir.Enumerate is a yield-based iterator, so its body (including the
                    // FindFirstFileEx open and any throw) runs lazily on the first MoveNext -- not on the call
                    // or GetEnumerator. That means a single try around the MoveNext loop covers both opening
                    // the directory and stepping it; a failure (directory gone, access denied, an entry
                    // vanished mid-scan) reports the directory and moves on to the next stacked directory. (If
                    // Enumerate ever becomes a non-iterator that opens eagerly, the open would need its own try.)
                    var entries = NativeDir.Enumerate(current, skipDirectories: !recurse).GetEnumerator();
                    try
                    {
                        while (true)
                        {
                            NativeDirEntry entry;
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
                                ReportError(current, ex);
                                break;
                            }

                            // Directory entries only arrive from the recursing (skipDirectories: false) path,
                            // so reaching this branch already implies recursion.
                            if (entry.IsDirectory)
                            {
                                // Follow a subdirectory unless it is a link we should not follow. The reparse
                                // check is by *tag* (IsNameSurrogate): real links (symlink/junction/mount point)
                                // are skipped under RecurseNoLinks, but non-link reparse points (cloud/dedup/
                                // ProjFS/WCI placeholders) are walked like ordinary directories.
                                if (DirectoryIsIncluded(directoryFilters, entry.Name) &&
                                    (followLinks || !entry.IsNameSurrogate))
                                {
                                    var childPath = Path.Combine(current, entry.Name);

                                    // When following links, TryEnterDirectory probes identity to break cycles
                                    // (and skips a followed directory it cannot identify, reporting it). When
                                    // not following links it is a no-op pass-through.
                                    if (TryEnterDirectory(childPath))
                                    {
                                        stack.Push(childPath);
                                    }
                                }
                            }
                            else if (fileFilters == null || fileFilters.ShouldInclude(entry.Name))
                            {
                                yield return Path.Combine(current, entry.Name);
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

        private static class NativeMethods
        {
            // GetFileType return values (winbase.h). Only FILE_TYPE_DISK is a searchable regular file;
            // FILE_TYPE_CHAR (console/LPT), FILE_TYPE_PIPE (pipe/socket/FIFO), and FILE_TYPE_UNKNOWN are not.
            public const uint FILE_TYPE_DISK = 0x0001;

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern uint GetFileType(SafeFileHandle hFile);
        }
    }
}
