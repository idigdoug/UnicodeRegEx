namespace UnicodeRegEx.Tests.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using System.Text;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;
    using UnicodeRegEx.Tests;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Engine;

    [TestClass]
    public class SearchJobTests
    {
        private string tempDir = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "urex_job_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private string WriteFile(string relativePath, string content)
        {
            var full = Path.Combine(tempDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content, new UTF8Encoding(false));
            return full;
        }

        private string WriteBytes(string relativePath, byte[] content)
        {
            var full = Path.Combine(tempDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, content);
            return full;
        }

        private static SearchRequest Request(string pattern, params string[] paths)
        {
            var request = new SearchRequest { Pattern = pattern, DefaultCodePage = RegExCodePage.Utf8 };
            foreach (var path in paths)
            {
                request.Paths.Add(path);
            }

            return request;
        }

        private static async Task<(RecordingSink Sink, SearchSummary Summary, SearchJobState State)> RunAsync(SearchRequest request)
        {
            var sink = new RecordingSink();
            using var job = new SearchJob(request, sink);
            await job.RunAsync();
            return (sink, job.Summary, job.State);
        }

        [TestMethod]
        public async Task Search_NamedFile_ReportsHits()
        {
            var file = WriteFile("a.txt", "alpha beta alpha");
            var (sink, summary, state) = await RunAsync(Request("alpha", file));

            Assert.AreEqual(SearchJobState.Completed, state);
            Assert.IsTrue(summary.AnyMatch);
            CollectionAssert.AreEqual(new[] { "alpha", "alpha" }, sink.HitTexts());
            Assert.AreEqual(0, summary.Errors);
        }

        [TestMethod]
        public async Task Parallel_ProducesSameAggregateResults_AsSerial()
        {
            // A spread of files, each with a known number of matches. The aggregate outcome must be
            // independent of the degree of parallelism (only per-file ordering may differ, which this
            // asserts against as a multiset).
            const int fileCount = 40;
            var expectedHits = 0;
            for (var i = 0; i < fileCount; i++)
            {
                var matches = (i % 3) + 1; // 1..3 matches per file
                WriteFile($"dir{i % 4}\\file{i}.txt", string.Join(" ", System.Linq.Enumerable.Repeat("m", matches)));
                expectedHits += matches;
            }

            async Task<int> RunWithDop(int dop)
            {
                var request = Request("m", tempDir);
                request.Directories = DirectoryDisposition.RecurseNoLinks;
                request.MaxDegreeOfParallelism = dop;
                var (sink, summary, state) = await RunAsync(request);
                Assert.AreEqual(SearchJobState.Completed, state);
                Assert.AreEqual(0, summary.Errors);
                return sink.Hits.Count;
            }

            var serial = await RunWithDop(1);
            var parallel = await RunWithDop(4);

            Assert.AreEqual(expectedHits, serial);
            Assert.AreEqual(expectedHits, parallel);
        }

        [TestMethod]
        public async Task Parallel_Automatic_FindsAllMatches()
        {
            for (var i = 0; i < 16; i++)
            {
                WriteFile($"file{i}.txt", "m m");
            }

            var request = Request("m", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            request.MaxDegreeOfParallelism = 0; // automatic (ProcessorCount)
            var (sink, summary, state) = await RunAsync(request);

            Assert.AreEqual(SearchJobState.Completed, state);
            Assert.AreEqual(32, sink.Hits.Count);
            Assert.AreEqual(0, summary.Errors);
        }

        [TestMethod]
        public async Task Parallel_Replace_ChangesAllFiles()
        {
            for (var i = 0; i < 16; i++)
            {
                WriteFile($"file{i}.txt", "aaa");
            }

            var request = Request("a", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            request.ReplaceTemplate = "b";
            request.Verb = SearchVerb.Apply;
            request.MaxDegreeOfParallelism = 4;
            var (_, summary, state) = await RunAsync(request);

            Assert.AreEqual(SearchJobState.Completed, state);
            Assert.AreEqual(16, summary.FilesChanged);
            for (var i = 0; i < 16; i++)
            {
                Assert.AreEqual("bbb", System.IO.File.ReadAllText(System.IO.Path.Combine(tempDir, $"file{i}.txt")));
            }
        }

        [TestMethod]
        public async Task Parallel_SinkThrows_FaultsTheJob_WithOriginalException()
        {
            for (var i = 0; i < 16; i++)
            {
                WriteFile($"file{i}.txt", "m");
            }

            var sink = new SteeringSink(throwOnHit: true);
            var request = Request("m", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            request.MaxDegreeOfParallelism = 4;
            using var job = new SearchJob(request, sink);

            Exception? caught = null;
            try
            {
                await job.RunAsync();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Even under parallelism, a throwing sink faults the job with the original exception (unwrapped
            // from the internal SinkException and from Parallel.ForEach's AggregateException).
            Assert.IsInstanceOfType(caught, typeof(InvalidOperationException));
            Assert.AreEqual("boom", caught!.Message);
            Assert.AreEqual(SearchJobState.Faulted, job.State);
        }

        [TestMethod]
        public async Task OnFileComplete_PairsWithNonSkippedOnFile()
        {
            WriteFile("match1.txt", "m");
            WriteFile("match2.txt", "m");
            WriteFile("nomatch.txt", "zzz"); // reported by OnFile, zero hits, still completes

            var request = Request("m", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            var (sink, _, _) = await RunAsync(request);

            // Every file OnFile accepted (returned Continue) is closed by exactly one OnFileComplete, over
            // the same SearchFile instances -- including the file that produced no hits.
            Assert.AreEqual(sink.Files.Count, sink.CompletedFiles.Count);
            CollectionAssert.AreEquivalent(sink.Files, sink.CompletedFiles);
            Assert.AreEqual(3, sink.CompletedFiles.Count);
        }

        [TestMethod]
        public async Task OnFileComplete_DoesNotFireForSkippedFile()
        {
            WriteFile("a.txt", "m");
            WriteFile("b.txt", "m");

            // Skip a.txt from OnFile; it must not receive an OnFileComplete (the bracket never opened).
            var aPath = System.IO.Path.Combine(tempDir, "a.txt");
            var sink = new SteeringSink(onFile: f => f.Path == aPath ? SearchResponse.StopFile : SearchResponse.Continue);
            var request = Request("m", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            using var job = new SearchJob(request, sink);
            await job.RunAsync();

            CollectionAssert.DoesNotContain(sink.CompletedPaths, aPath);
            Assert.IsTrue(sink.CompletedPaths.Contains(System.IO.Path.Combine(tempDir, "b.txt")));
        }

        // ---- OnFile: bytes, code-page override, and Context

        // Bytes 0x41 0xC3 0xA9 0x42: "A", the UTF-8 encoding of "é", then "B".
        //   As UTF-8   => "AéB"
        //   As Latin1  => "AÃ©B"  (0xC3='Ã', 0xA9='©')
        private static readonly byte[] AmbiguousBytes = { 0x41, 0xC3, 0xA9, 0x42 };

        // A sink that runs a caller-supplied action during OnFile (e.g. to override the code page or set
        // Context) and records every hit's text and the SearchFile it came from.
        private sealed class OnFileSink : SearchSinkBase
        {
            private readonly Action<SearchFile>? onFile;

            public OnFileSink(Action<SearchFile>? onFile = null) => this.onFile = onFile;

            public List<string> HitTexts { get; } = new List<string>();
            public List<object?> HitContexts { get; } = new List<object?>();

            public override SearchResponse OnFile(SearchFile file, RegExPinnedBytes bytes)
            {
                onFile?.Invoke(file);
                return SearchResponse.Continue;
            }

            public override SearchResponse OnMatch(in SearchHit hit)
            {
                HitTexts.Add(hit.Text);
                HitContexts.Add(hit.File.Context);
                return SearchResponse.Continue;
            }
        }

        [TestMethod]
        public async Task OnFile_DefaultCodePage_DecodesAsRequested()
        {
            // Baseline: with the request's UTF-8 default and no override, the bytes decode as "AéB", so a
            // pattern matching the Latin1 interpretation ("Ã©") finds nothing.
            var path = WriteBytes("a.txt", AmbiguousBytes);
            var sink = new OnFileSink();
            using var job = new SearchJob(Request("Ã©", path), sink);
            await job.RunAsync();

            Assert.AreEqual(0, sink.HitTexts.Count);
        }

        [TestMethod]
        public async Task OnFile_OverrideCodePage_ChangesDecoding()
        {
            // Overriding to Latin1 in OnFile makes the same bytes decode as "AÃ©B", so "Ã©" now matches.
            var path = WriteBytes("a.txt", AmbiguousBytes);
            var sink = new OnFileSink(f => f.OverrideCodePage(RegExCodePage.Latin1));
            using var job = new SearchJob(Request("Ã©", path), sink);
            await job.RunAsync();

            Assert.AreEqual(1, sink.HitTexts.Count);
            Assert.AreEqual("Ã©", sink.HitTexts[0]);
        }

        [TestMethod]
        public async Task OnFile_OverrideCodePage_AfterLock_Throws()
        {
            // Capture the SearchFile during OnFile, then try to override from OnHit (after the file is
            // locked). The override must throw; a thrown sink faults the job with the original exception.
            var path = WriteBytes("a.txt", AmbiguousBytes);
            SearchFile? captured = null;
            var sink = new LockViolationSink(f => captured = f);
            using var job = new SearchJob(Request("A", path), sink);

            Exception? caught = null;
            try
            {
                await job.RunAsync();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.IsInstanceOfType(caught, typeof(InvalidOperationException));
            Assert.AreEqual(SearchJobState.Faulted, job.State);
            Assert.IsNotNull(captured);
            Assert.IsTrue(captured!.IsLocked);
        }

        [TestMethod]
        public void OverrideCodePage_UnsupportedCodePage_Throws()
        {
            // Direct unit check: an unsupported code page is rejected before it can reach the engine.
            var file = new SearchFile("x", RegExCodePage.Utf8, looksBinary: false);
            TestHelpers.AssertThrows<ArgumentException>(() => file.OverrideCodePage(999999));
        }

        [TestMethod]
        public async Task OnFile_Context_FlowsToOnHit()
        {
            var path = WriteFile("a.txt", "match match");
            var marker = new object();
            var sink = new OnFileSink(f => f.Context = marker);
            using var job = new SearchJob(Request("match", path), sink);
            await job.RunAsync();

            Assert.AreEqual(2, sink.HitContexts.Count);
            Assert.AreSame(marker, sink.HitContexts[0]);
            Assert.AreSame(marker, sink.HitContexts[1]);
        }

        [TestMethod]
        public async Task OnFile_ReceivesFileBytes()
        {
            var content = "hello world";
            var path = WriteFile("a.txt", content);
            var sink = new BytesCapturingSink();
            using var job = new SearchJob(Request("world", path), sink);
            await job.RunAsync();

            Assert.AreEqual(Encoding.UTF8.GetByteCount(content), sink.ByteCount);
            Assert.AreEqual(content, sink.DecodedContent);
        }

        // Overrides the code page from OnHit (after lock) to verify the lock is enforced.
        private sealed class LockViolationSink : SearchSinkBase
        {
            private readonly Action<SearchFile> capture;

            public LockViolationSink(Action<SearchFile> capture) => this.capture = capture;

            public override SearchResponse OnFile(SearchFile file, RegExPinnedBytes fileBytes)
            {
                capture(file);
                return SearchResponse.Continue;
            }

            public override SearchResponse OnMatch(in SearchHit hit)
            {
                // The file is locked once OnFile returned; this must throw.
                hit.File.OverrideCodePage(RegExCodePage.Latin1);
                return SearchResponse.Continue;
            }
        }

        // Copies the OnFile bytes out (decoded UTF-8) so a test can assert the raw content was passed.
        private sealed class BytesCapturingSink : SearchSinkBase
        {
            public int ByteCount { get; private set; }
            public string DecodedContent { get; private set; } = string.Empty;

            public override unsafe SearchResponse OnFile(SearchFile file, RegExPinnedBytes fileBytes)
            {
                ByteCount = fileBytes.SizeInt;
                DecodedContent = Encoding.UTF8.GetString(fileBytes.DataPtr, fileBytes.SizeInt);
                return SearchResponse.Continue;
            }
        }

        // ---- Syntax-flag tuning (to-do #3)

        [TestMethod]
        public async Task DotAll_Off_DotDoesNotCrossNewline()
        {
            var file = WriteFile("a.txt", "a\nb");

            var (sink, _, _) = await RunAsync(Request("a.b", file)); // DotAll defaults off

            Assert.AreEqual(0, sink.Hits.Count);
        }

        [TestMethod]
        public async Task DotAll_On_DotMatchesAcrossNewline()
        {
            var file = WriteFile("a.txt", "a\nb");
            var request = Request("a.b", file);
            request.SetSyntaxFlags(RegExSyntaxFlags.Perl, dotAll: true);

            var (sink, _, _) = await RunAsync(request);

            Assert.AreEqual(1, sink.Hits.Count);
        }

        [TestMethod]
        public async Task MultilineAnchors_Default_CaretMatchesAfterEmbeddedNewline()
        {
            var file = WriteFile("a.txt", "a\nb");

            var (sink, _, _) = await RunAsync(Request("^b", file)); // MultilineAnchors defaults on

            Assert.AreEqual(1, sink.Hits.Count);
        }

        [TestMethod]
        public async Task MultilineAnchors_Off_CaretMatchesOnlyAtInputStart()
        {
            var file = WriteFile("a.txt", "a\nb");
            var request = Request("^b", file);
            request.SetSyntaxFlags(RegExSyntaxFlags.Perl, multilineAnchors: false);

            var (sink, _, _) = await RunAsync(request);

            Assert.AreEqual(0, sink.Hits.Count);
        }

        [TestMethod]
        public async Task ZeroLengthFile_ZeroLengthPattern_Matches()
        {
            var file = WriteFile("empty.txt", string.Empty); // 0 bytes

            var (sink, summary, state) = await RunAsync(Request("^", file));

            Assert.AreEqual(SearchJobState.Completed, state);
            Assert.AreEqual(1, sink.Hits.Count); // "^" matches the single empty position
            Assert.AreEqual(0, summary.Errors);  // an empty regular file is not an error
        }

        [TestMethod]
        public async Task ZeroLengthFile_NonMatchingPattern_NoHitsNoErrors()
        {
            var file = WriteFile("empty.txt", string.Empty);

            var (sink, summary, _) = await RunAsync(Request("zzz", file));

            Assert.AreEqual(0, sink.Hits.Count);
            Assert.AreEqual(0, summary.Errors);
        }

        [TestMethod]
        public async Task ZeroLengthFile_Replace_ProducesReplacement()
        {
            var file = WriteFile("empty.txt", string.Empty);
            var request = Request("^", file);
            request.Verb = SearchVerb.Apply;
            request.ReplaceTemplate = "X";
            var (_, summary, _) = await RunAsync(request);

            Assert.AreEqual(1, summary.FilesChanged);
            Assert.AreEqual("X", File.ReadAllText(file)); // the empty file gains the replacement
        }

        [TestMethod]
        public async Task SpecialFile_IsReportedAsError()
        {
            // A named pipe is a non-disk file: the engine must report it as an error rather than treat it
            // as a searchable (empty) file. Requires the server to be listening for the search's open.
            var pipeName = "urex_test_" + Guid.NewGuid().ToString("N");
            var pipePath = @"\\.\pipe\" + pipeName;

            System.IO.Pipes.NamedPipeServerStream server;
            try
            {
                server = new System.IO.Pipes.NamedPipeServerStream(
                    pipeName, System.IO.Pipes.PipeDirection.Out, 1);
            }
            catch
            {
                Assert.Inconclusive("Could not create a named pipe on this system.");
                return;
            }

            try
            {
                var waitForConnection = server.WaitForConnectionAsync();

                var (sink, summary, _) = await RunAsync(Request("anything", pipePath));

                // The pipe is a non-disk file: it is reported as an error, never searched as a file.
                Assert.AreEqual(0, sink.Hits.Count);
                Assert.AreEqual(1, sink.Errors.Count);
                Assert.IsInstanceOfType(sink.Errors[0].Exception, typeof(IOException));
                Assert.AreEqual(1, summary.Errors);

                if (server.IsConnected)
                {
                    server.Disconnect();
                }

                _ = waitForConnection;
            }
            finally
            {
                server.Dispose();
            }
        }

        [TestMethod]
        public async Task MatchFlags_NotBol_SuppressesCaretAtBufferStart()
        {
            var file = WriteFile("a.txt", "b"); // "^b" would match at the buffer start by default

            var request = Request("^b", file);
            request.MatchFlags = RegExMatchFlags.NotBol;

            var (sink, _, _) = await RunAsync(request);

            Assert.AreEqual(0, sink.Hits.Count); // NotBol suppresses "^" at the start of the input
        }

        [TestMethod]
        public async Task MatchFlags_Default_CaretMatchesAtBufferStart()
        {
            var file = WriteFile("a.txt", "b");

            var (sink, _, _) = await RunAsync(Request("^b", file)); // MatchFlags defaults to Default

            Assert.AreEqual(1, sink.Hits.Count);
        }

        [TestMethod]
        public async Task Search_NoMatch_CompletesWithoutHits()
        {
            var file = WriteFile("a.txt", "nothing here");
            var (sink, summary, state) = await RunAsync(Request("zzz", file));

            Assert.AreEqual(SearchJobState.Completed, state);
            Assert.IsFalse(summary.AnyMatch);
            Assert.AreEqual(0, sink.Hits.Count);
        }

        [TestMethod]
        public async Task Search_Directory_ReadImmediateFiles_OnlyTopLevel()
        {
            WriteFile("top.txt", "match");
            WriteFile("sub\\nested.txt", "match");

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.ReadImmediateFiles;
            var (sink, summary, _) = await RunAsync(request);

            Assert.IsTrue(summary.AnyMatch);
            Assert.AreEqual(1, sink.Hits.Count, "only the top-level file should be searched");
        }

        [TestMethod]
        public async Task Search_Directory_Recursive_IncludesNested()
        {
            WriteFile("top.txt", "match");
            WriteFile("sub\\nested.txt", "match");

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            var (sink, summary, _) = await RunAsync(request);

            Assert.IsTrue(summary.AnyMatch);
            Assert.AreEqual(2, sink.Hits.Count, "both files should be searched recursively");
        }

        [TestMethod]
        public async Task Search_IncludeGlob_FiltersDirectoryWalkedFiles()
        {
            WriteFile("keep.cs", "match");
            WriteFile("skip.txt", "match");

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.ReadImmediateFiles;
            request.AddIncludeFileGlobs("*.cs");
            var (sink, _, _) = await RunAsync(request);

            Assert.AreEqual(1, sink.Hits.Count);
        }

        [TestMethod]
        public async Task Search_ExcludeFilter_PrunesMatchingFiles()
        {
            WriteFile("keep.cs", "match");
            WriteFile("skip.txt", "match");

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.ReadImmediateFiles;
            // First filter is Exclude, so unmatched names default to included: keep.cs is searched, skip.txt is not.
            request.FileNameFilters.Add(new GlobFilter(FilterKind.Exclude, "*.txt"));
            var (sink, _, _) = await RunAsync(request);

            Assert.AreEqual(1, sink.Hits.Count);
            Assert.IsTrue(sink.Files[0].Path.EndsWith("keep.cs"));
        }

        [TestMethod]
        public async Task Recurse_ExcludeDir_PrunesSubtree()
        {
            WriteFile("top.cs", "match");
            WriteFile("skip\\inner.cs", "match");
            WriteFile("keep\\inner.cs", "match");

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            request.AddExcludeDirGlobs("skip");
            var (sink, _, _) = await RunAsync(request);

            // top.cs + keep/inner.cs are searched; skip/ is never descended.
            Assert.AreEqual(2, sink.Hits.Count);
            Assert.IsFalse(sink.Files.Exists(f => f.Path.Contains("skip")));
        }

        [TestMethod]
        public async Task Recurse_DirFilters_UnmatchedDirectoryStillDescended()
        {
            // Option B: a leading include must NOT default-exclude descent, so a sibling directory that
            // doesn't match the include is still walked.
            WriteFile("onlythis\\a.cs", "match");
            WriteFile("sibling\\b.cs", "match");

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            request.DirectoryFilters.Add(new GlobFilter(FilterKind.Include, "onlythis"));
            var (sink, _, _) = await RunAsync(request);

            Assert.AreEqual(2, sink.Hits.Count); // both subdirectories are descended
        }

        [TestMethod]
        public async Task ReadImmediateFiles_DoesNotRecurse()
        {
            WriteFile("top.cs", "match");
            WriteFile("sub\\inner.cs", "match");

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.ReadImmediateFiles;
            var (sink, _, _) = await RunAsync(request);

            // Only the top-level file; subdirectories are not descended.
            Assert.AreEqual(1, sink.Hits.Count);
            Assert.IsTrue(sink.Files[0].Path.EndsWith("top.cs"));
        }

        [TestMethod]
        public async Task Recurse_Root_IsFilteredByDirectoryFilters()
        {
            WriteFile("top.cs", "match");
            WriteFile("child\\inner.cs", "match");

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            // Directory filters apply to the root too (matching grep): an exclude that matches the root's
            // own name prunes the whole search, so nothing is searched.
            request.DirectoryFilters.Add(new GlobFilter(FilterKind.Exclude, "*"));
            var (sink, _, _) = await RunAsync(request);

            Assert.AreEqual(0, sink.Files.Count);
        }

        [TestMethod]
        public async Task Directory_DefaultDisposition_ReportsIsADirectory()
        {
            WriteFile("top.cs", "match");

            var request = Request("match", tempDir); // Directories defaults to Error

            var (sink, summary, _) = await RunAsync(request);

            Assert.AreEqual(0, sink.Hits.Count);
            Assert.AreEqual(1, sink.Errors.Count);
            Assert.AreEqual(tempDir, sink.Errors[0].Path);
            Assert.IsInstanceOfType(sink.Errors[0].Exception, typeof(IOException));
            Assert.AreEqual("Is a directory", sink.Errors[0].Exception.Message);
            Assert.AreEqual(1, summary.Errors);
        }

        [TestMethod]
        public async Task Directory_Skip_IsSilentlyIgnored()
        {
            WriteFile("top.cs", "match");

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.Skip;

            var (sink, _, _) = await RunAsync(request);

            Assert.AreEqual(0, sink.Hits.Count);
            Assert.AreEqual(0, sink.Errors.Count);
        }

        [TestMethod]
        public async Task RecurseNoLinks_SkipsJunctionedDirectory()
        {
            WriteFile("real\\inner.cs", "match");
            var target = Path.Combine(tempDir, "real");
            var link = Path.Combine(tempDir, "link");
            if (!TryCreateJunction(link, target))
            {
                Assert.Inconclusive("Could not create a directory junction on this system.");
            }

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;

            var (sink, _, _) = await RunAsync(request);

            // real/inner.cs matches once; the junction is a reparse point and is not descended, so the
            // same file is not found a second time through "link".
            Assert.AreEqual(1, sink.Hits.Count);
        }

        [TestMethod]
        public async Task RecurseWithLinks_FollowedJunction_IsDeduplicatedByIdentity()
        {
            WriteFile("real\\inner.cs", "match");
            var target = Path.Combine(tempDir, "real");
            var link = Path.Combine(tempDir, "link");
            if (!TryCreateJunction(link, target))
            {
                Assert.Inconclusive("Could not create a directory junction on this system.");
            }

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.RecurseWithLinks;

            var (sink, _, _) = await RunAsync(request);

            // "real" and the junction "link" resolve to the same directory. RecurseWithLinks follows the
            // link, but the walk de-duplicates by directory identity (volume serial + file id), so the target
            // is searched exactly once regardless of how many links point at it -- inner.cs is found once, not
            // once per path. (This is the deliberate visited-set policy: a search never reports the same
            // file's matches twice. Cycle prevention falls out of the same mechanism.)
            Assert.AreEqual(1, sink.Hits.Count);
        }

        [TestMethod]
        public async Task RecurseWithLinks_CycleIsBroken()
        {
            // real/inner.cs plus a junction real/loop -> the search root, forming a cycle
            // (root -> real -> loop -> root -> ...). RecurseWithLinks follows links, so without identity-based
            // cycle detection this would recurse forever. It must instead terminate and search each real file
            // a bounded number of times.
            WriteFile("real\\inner.cs", "match");
            var loop = Path.Combine(tempDir, "real", "loop");
            if (!TryCreateJunction(loop, tempDir))
            {
                Assert.Inconclusive("Could not create a directory junction on this system.");
            }

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.RecurseWithLinks;

            var (sink, summary, state) = await RunAsync(request);

            // The run completes (does not hang) and the cycle is cut by identity: the root is recorded, so
            // when the junction "loop" (which resolves back to the root) is encountered it is recognized as
            // already-visited and not descended. inner.cs is therefore found exactly once.
            Assert.AreEqual(SearchJobState.Completed, state);
            Assert.AreEqual(1, sink.Hits.Count);
        }

        // Creates a directory junction (mklink /J) without requiring symlink privilege. Returns false if
        // the OS/environment does not support it, so callers can mark the test inconclusive.
        private static bool TryCreateJunction(string link, string target)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using var process = System.Diagnostics.Process.Start(psi);
                process!.WaitForExit();
                return process.ExitCode == 0 && Directory.Exists(link);
            }
            catch
            {
                return false;
            }
        }

        [TestMethod]
        public async Task Search_NamedFile_BypassesIncludeGlob()
        {
            // An explicitly named file is always searched, even if it doesn't match --include.
            var file = WriteFile("data.txt", "match");
            var request = Request("match", file);
            request.AddIncludeFileGlobs("*.cs");
            var (sink, _, _) = await RunAsync(request);

            Assert.AreEqual(1, sink.Hits.Count);
        }

        [TestMethod]
        public async Task ApplyReplace_RewritesFile_AndReportsChange()
        {
            var file = WriteFile("a.txt", "alpha beta alpha");
            var request = Request("alpha", file);
            request.Verb = SearchVerb.Apply;
            request.ReplaceTemplate = "X";
            var (sink, summary, state) = await RunAsync(request);

            Assert.AreEqual(SearchJobState.Completed, state);
            Assert.AreEqual(1, summary.FilesChanged);
            CollectionAssert.Contains(sink.ChangedFiles, file);
            Assert.AreEqual("X beta X", File.ReadAllText(file));

            // Apply mode also reports the processed file via OnFile (with its detected encoding).
            Assert.AreEqual(1, sink.Files.Count);
            Assert.AreEqual(file, sink.Files[0].Path);
            Assert.AreEqual(RegExCodePage.Utf8, sink.Files[0].CodePage);
        }

        // Drives OnApply per match with a caller-supplied action. Uses a custom delegate (not Func<,>)
        // because SearchHit is a ref struct and cannot be a Func type argument.
        private delegate ApplyAction ApplyDecider(in SearchHit hit);

        private sealed class ApplySink : SearchSinkBase
        {
            private readonly ApplyDecider onApply;

            public ApplySink(ApplyDecider onApply) => this.onApply = onApply;

            public override ApplyAction OnApply(in SearchHit hit) => onApply(in hit);
        }

        private async Task RunApply(string path, string pattern, ApplyDecider onApply)
        {
            var request = Request(pattern, path);
            request.Verb = SearchVerb.Apply;
            request.ReplaceTemplate = "X"; // the Default action would write this
            using var job = new SearchJob(request, new ApplySink(onApply));
            await job.RunAsync();
            Assert.AreEqual(SearchJobState.Completed, job.State);
        }

        [TestMethod]
        public async Task OnApply_Default_WritesFormattedReplacement()
        {
            var file = WriteFile("a.txt", "aa bb aa");
            await RunApply(file, "aa", (in SearchHit _) => ApplyAction.Default);
            Assert.AreEqual("X bb X", File.ReadAllText(file));
        }

        [TestMethod]
        public async Task OnApply_Original_LeavesMatchUnchanged()
        {
            var file = WriteFile("a.txt", "aa bb aa");
            await RunApply(file, "aa", (in SearchHit _) => ApplyAction.Original);
            Assert.AreEqual("aa bb aa", File.ReadAllText(file));
        }

        [TestMethod]
        public async Task OnApply_Delete_RemovesMatch()
        {
            var file = WriteFile("a.txt", "aaXbbXaa"); // delete each "X"
            await RunApply(file, "X", (in SearchHit _) => ApplyAction.Delete);
            Assert.AreEqual("aabbaa", File.ReadAllText(file));
        }

        [TestMethod]
        public async Task OnApply_Custom_WritesComputedBytes()
        {
            // Each match is replaced by caller-computed bytes (here the match's own text upper-cased, in the
            // file's UTF-8 code page). Proves the computed-replacement path end to end.
            var file = WriteFile("a.txt", "one two one");
            await RunApply(file, "one", (in SearchHit hit) =>
            {
                var bytes = Encoding.UTF8.GetBytes(hit.Text.ToUpperInvariant());
                return ApplyAction.Custom(new ArraySegment<byte>(bytes));
            });
            Assert.AreEqual("ONE two ONE", File.ReadAllText(file));
        }

        [TestMethod]
        public async Task OnApply_Mixed_PerMatchDecisions()
        {
            // Different action per match, proving OnApply is consulted per match and the choices compose in
            // the output stream.
            var file = WriteFile("a.txt", "[a][b][c]");
            var index = 0;
            await RunApply(file, "[a-c]", (in SearchHit hit) =>
            {
                var i = index++;
                if (i == 0)
                {
                    return ApplyAction.Custom(new ArraySegment<byte>(Encoding.UTF8.GetBytes("1")));
                }

                return i == 1 ? ApplyAction.Delete : ApplyAction.Original;
            });
            // "a" -> "1", "b" -> "" (deleted), "c" -> "c" (unchanged)
            Assert.AreEqual("[1][][c]", File.ReadAllText(file));
        }

        [TestMethod]
        public async Task FormatFlags_Sed_TreatsAmpersandAsWholeMatch()
        {
            // In sed replacement syntax '&' means the whole match, so "[&]" wraps each match. This proves
            // SearchRequest.FormatFlags propagates through to the engine's replace path.
            var file = WriteFile("a.txt", "abc def");
            var request = Request("[a-z]+", file);
            request.Verb = SearchVerb.Apply;
            request.ReplaceTemplate = "[&]";
            request.FormatFlags = RegExFormatFlags.Sed;
            var (_, summary, state) = await RunAsync(request);

            Assert.AreEqual(SearchJobState.Completed, state);
            Assert.AreEqual(1, summary.FilesChanged);
            Assert.AreEqual("[abc] [def]", File.ReadAllText(file));
        }

        [TestMethod]
        public async Task FormatFlags_DefaultPerl_TreatsAmpersandLiterally()
        {
            // The default (Perl) format does not treat '&' specially, so "[&]" is written verbatim --
            // contrasting FormatFlags_Sed to show the flag actually changes behavior.
            var file = WriteFile("a.txt", "abc def");
            var request = Request("[a-z]+", file);
            request.Verb = SearchVerb.Apply;
            request.ReplaceTemplate = "[&]";
            // request.FormatFlags stays RegExFormatFlags.Perl (the default).
            var (_, _, state) = await RunAsync(request);

            Assert.AreEqual(SearchJobState.Completed, state);
            Assert.AreEqual("[&] [&]", File.ReadAllText(file));
        }

        [TestMethod]
        public async Task ApplyReplace_NoMatch_LeavesFileUntouched()
        {
            var file = WriteFile("a.txt", "no matches here");
            var before = File.ReadAllText(file);

            var request = Request("zzz", file);
            request.Verb = SearchVerb.Apply;
            request.ReplaceTemplate = "X";
            var (_, summary, _) = await RunAsync(request);

            Assert.AreEqual(0, summary.FilesChanged);
            Assert.AreEqual(before, File.ReadAllText(file));
        }

        [TestMethod]
        public async Task ApplyReplace_ReportsHitPerMatch_WithReplacement()
        {
            var file = WriteFile("a.txt", "alpha beta alpha");
            var request = Request("alpha", file);
            request.Verb = SearchVerb.Apply;
            request.ReplaceTemplate = "X";
            var (sink, summary, _) = await RunAsync(request);

            // Apply mode now reports a hit per match (symmetry with search), each exposing its replacement.
            Assert.AreEqual(2, sink.Hits.Count);
            foreach (var hit in sink.Hits)
            {
                Assert.AreEqual("alpha", hit.Text);
                Assert.AreEqual("X", hit.Replacement);
            }

            Assert.AreEqual(1, summary.FilesChanged);
            Assert.AreEqual("X beta X", File.ReadAllText(file));
        }

        [TestMethod]
        public async Task ApplyReplace_OnHitStopFile_AbandonsFile_ButContinuesToNextFile()
        {
            var a = WriteFile("a.txt", "alpha alpha");
            var b = WriteFile("b.txt", "alpha alpha");

            // Stop on the very first hit (in file a), then let the rest through.
            var hitCount = 0;
            var sink = new SteeringSink(onHit: _ =>
            {
                hitCount++;
                return hitCount == 1 ? SearchResponse.StopFile : SearchResponse.Continue;
            });

            var request = Request("alpha", a, b);
            request.Verb = SearchVerb.Apply;
            request.ReplaceTemplate = "X";
            using var job = new SearchJob(request, sink);
            await job.RunAsync();

            Assert.AreEqual(SearchJobState.Completed, job.State);
            Assert.AreEqual("alpha alpha", File.ReadAllText(a));  // abandoned: original untouched
            Assert.AreEqual("X X", File.ReadAllText(b));          // next file still rewritten
            Assert.AreEqual(1, job.Summary.FilesChanged);
        }

        [TestMethod]
        public async Task ApplyReplace_OnHitStopAll_CancelsJob_LeavesFilesUnchanged()
        {
            var a = WriteFile("a.txt", "alpha alpha");
            var b = WriteFile("b.txt", "alpha alpha");

            var sink = new SteeringSink(onHit: _ => SearchResponse.StopAll);

            var request = Request("alpha", a, b);
            request.Verb = SearchVerb.Apply;
            request.ReplaceTemplate = "X";
            using var job = new SearchJob(request, sink);
            await job.RunAsync();

            Assert.AreEqual(SearchJobState.Canceled, job.State);
            Assert.IsTrue(job.Summary.Cancelled);
            Assert.AreEqual(0, job.Summary.FilesChanged);
            Assert.AreEqual("alpha alpha", File.ReadAllText(a));  // abandoned before commit
            Assert.AreEqual("alpha alpha", File.ReadAllText(b));  // never processed
        }

        [TestMethod]
        public async Task MissingPath_ReportsError()
        {
            var missing = Path.Combine(tempDir, "does-not-exist.txt");
            var (sink, summary, state) = await RunAsync(Request("x", missing));

            Assert.AreEqual(SearchJobState.Completed, state);
            Assert.AreEqual(1, sink.Errors.Count);
            Assert.AreEqual(missing, sink.Errors[0].Path);
        }

        [TestMethod]
        public async Task InvalidPattern_FaultsTheTask_AndSetsFaultedState()
        {
            var file = WriteFile("a.txt", "text");
            var request = Request("(unclosed", file);

            var sink = new RecordingSink();
            using var job = new SearchJob(request, sink);

            await TestHelpers.AssertThrowsAsync<RegExException>(() => job.RunAsync());
            Assert.AreEqual(SearchJobState.Faulted, job.State);
        }

        [TestMethod]
        public async Task RunAsync_CalledTwice_Throws()
        {
            var file = WriteFile("a.txt", "text");
            var sink = new RecordingSink();
            using var job = new SearchJob(Request("text", file), sink);

            await job.RunAsync();

            TestHelpers.AssertThrows<InvalidOperationException>(() => job.RunAsync());
        }

        [TestMethod]
        public async Task Cancel_BeforeRun_EndsCanceled()
        {
            var file = WriteFile("a.txt", "match");
            var sink = new RecordingSink();
            using var job = new SearchJob(Request("match", file), sink);

            job.Cancel();
            await job.RunAsync();

            Assert.AreEqual(SearchJobState.Canceled, job.State);
            Assert.IsTrue(job.Summary.Cancelled);
        }

        [TestMethod]
        public async Task Completed_FileCounts_AreConsistent()
        {
            WriteFile("a.txt", "match");
            WriteFile("b.txt", "match");

            var request = Request("match", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            var sink = new RecordingSink();
            using var job = new SearchJob(request, sink);
            await job.RunAsync();

            Assert.AreEqual(SearchJobState.Completed, job.State);
            Assert.AreEqual(2, job.TotalFileCount);
            Assert.AreEqual(job.TotalFileCount, job.CompletedFileCount);
        }

        // ---- Binary-file disposition (slice 4)

        // Writes a file whose bytes contain a NUL (so detection judges it binary) but whose ASCII text
        // still contains the given pattern, so a Search disposition can match it.
        private string WriteBinaryFile(string relativePath, string asciiBefore, string asciiAfter)
        {
            var full = Path.Combine(tempDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var before = Encoding.ASCII.GetBytes(asciiBefore);
            var after = Encoding.ASCII.GetBytes(asciiAfter);
            var bytes = new byte[before.Length + 1 + after.Length];
            Array.Copy(before, 0, bytes, 0, before.Length);
            bytes[before.Length] = 0x00; // NUL -> binary
            Array.Copy(after, 0, bytes, before.Length + 1, after.Length);
            File.WriteAllBytes(full, bytes);
            return full;
        }

        [TestMethod]
        public async Task BinarySkip_Default_NoHitsNoErrors()
        {
            var file = WriteBinaryFile("bin.dat", "alpha", "alpha");

            var (sink, summary, _) = await RunAsync(Request("alpha", file)); // binary files are skipped by default

            Assert.AreEqual(0, sink.Hits.Count);
            Assert.AreEqual(0, sink.Errors.Count);
            Assert.IsFalse(summary.AnyMatch);
        }

        [TestMethod]
        public async Task BinarySearch_SearchesAnyway_FindsMatches()
        {
            var file = WriteBinaryFile("bin.dat", "alpha", "alpha");
            var request = Request("alpha", file);
            request.SkipBinaryFiles = false;

            var (sink, summary, _) = await RunAsync(request);

            Assert.AreEqual(2, sink.Hits.Count); // both "alpha" occurrences
            Assert.AreEqual(0, sink.Errors.Count);
            Assert.IsTrue(summary.AnyMatch);
        }

        // ---- SearchFile / OnFile (slice 5)

        [TestMethod]
        public async Task OnFile_ReportsSearchedFile_WithMetadata_AndHitsShareIt()
        {
            var file = WriteFile("a.txt", "alpha beta alpha"); // UTF-8 text, two matches

            var (sink, _, _) = await RunAsync(Request("alpha", file));

            Assert.AreEqual(1, sink.Files.Count);
            var searched = sink.Files[0];
            Assert.AreEqual(file, searched.Path);
            Assert.AreEqual(RegExCodePage.Utf8, searched.CodePage);
            Assert.IsFalse(searched.LooksBinary);

            // Every hit references the same SearchFile instance reported by OnFile.
            Assert.AreEqual(2, sink.Hits.Count);
            foreach (var hit in sink.Hits)
            {
                Assert.AreSame(searched, hit.File);
            }
        }

        [TestMethod]
        public async Task OnFile_ZeroHitFile_IsStillReported()
        {
            var file = WriteFile("a.txt", "nothing to find here");

            var (sink, _, _) = await RunAsync(Request("zzz", file));

            Assert.AreEqual(1, sink.Files.Count); // the searched file is reported even with no hits
            Assert.AreEqual(file, sink.Files[0].Path);
            Assert.AreEqual(0, sink.Hits.Count);
        }

        [TestMethod]
        public async Task OnFile_SkippedBinaryFile_IsNotReported()
        {
            var file = WriteBinaryFile("bin.dat", "alpha", "alpha"); // binary files are skipped by default

            var (sink, _, _) = await RunAsync(Request("alpha", file));

            Assert.AreEqual(0, sink.Files.Count); // skipped files are not "processed"
            Assert.AreEqual(0, sink.Hits.Count);
        }

        [TestMethod]
        public async Task OnFile_BinarySearchedAnyway_IsReportedAsBinary()
        {
            var file = WriteBinaryFile("bin.dat", "alpha", "alpha");
            var request = Request("alpha", file);
            request.SkipBinaryFiles = false;

            var (sink, _, _) = await RunAsync(request);

            Assert.AreEqual(1, sink.Files.Count);
            Assert.IsTrue(sink.Files[0].LooksBinary);
            Assert.AreSame(sink.Files[0], sink.Hits[0].File);
        }

        [TestMethod]
        public async Task EncodingDetection_FromRequest_DisablingBinaryChecks_StopsBinarySkip()
        {
            // With binary detection steps disabled via the request, a NUL file is no longer judged
            // binary, so it is searched (and reported) instead of skipped.
            var file = WriteBinaryFile("bin.dat", "alpha", "alpha");
            var request = Request("alpha", file);
            request.EncodingDetection = new EncodingDetectionOptions(
                EncodingDetectionSteps.All & ~EncodingDetectionSteps.BinaryNul & ~EncodingDetectionSteps.BinaryControlRatio);

            var (sink, summary, _) = await RunAsync(request);

            Assert.AreEqual(1, sink.Files.Count);
            Assert.IsFalse(sink.Files[0].LooksBinary);
            Assert.IsTrue(summary.AnyMatch);
        }

        // Captures each hit's match byte-offset span during OnHit (the ref-struct hit can't be stored).
        private sealed class OffsetCapturingSink : SearchSinkBase
        {
            public List<(nuint Begin, nuint Size)> Spans { get; } = new List<(nuint, nuint)>();

            public override SearchResponse OnMatch(in SearchHit hit)
            {
                var whole = hit.Match.GetSubMatch(0);
                Spans.Add((whole.Begin, whole.Size));
                return SearchResponse.Continue;
            }
        }

        [TestMethod]
        public async Task Hit_ExposesMatch_ForByteOffsets()
        {
            var file = WriteFile("a.txt", "alpha beta alpha"); // "alpha" at bytes 0 and 11 (UTF-8/ASCII)
            var sink = new OffsetCapturingSink();
            using var job = new SearchJob(Request("alpha", file), sink);
            await job.RunAsync();

            CollectionAssert.AreEqual(
                new[] { ((nuint)0, (nuint)5), ((nuint)11, (nuint)5) },
                sink.Spans);
        }

        // A sink whose OnHit/OnFile responses (and optional throws) are driven by the test.
        private sealed class SteeringSink : SearchSinkBase
        {
            private readonly Func<string, SearchResponse>? onHit;
            private readonly Func<SearchFile, SearchResponse>? onFile;
            private readonly bool throwOnHit;
            private readonly object gate = new object();

            public SteeringSink(
                Func<string, SearchResponse>? onHit = null,
                Func<SearchFile, SearchResponse>? onFile = null,
                bool throwOnHit = false)
            {
                this.onHit = onHit;
                this.onFile = onFile;
                this.throwOnHit = throwOnHit;
            }

            public List<string> HitTexts { get; } = new List<string>();
            public List<string> FilePaths { get; } = new List<string>();
            public List<string> CompletedPaths { get; } = new List<string>();

            public override SearchResponse OnFile(SearchFile file, RegExPinnedBytes fileBytes)
            {
                lock (gate)
                {
                    FilePaths.Add(file.Path);
                }

                return onFile?.Invoke(file) ?? SearchResponse.Continue;
            }

            public override SearchResponse OnMatch(in SearchHit hit)
            {
                return Steer(hit);
            }

            public override ApplyAction OnApply(in SearchHit hit)
            {
                // Reuse the same steering logic; map the response onto an apply action (Continue = write the
                // default replacement, Stop* = abandon the file's rewrite).
                switch (Steer(hit))
                {
                    case SearchResponse.StopFile:
                        return ApplyAction.StopFile;
                    case SearchResponse.StopAll:
                        return ApplyAction.StopAll;
                    default:
                        return ApplyAction.Default;
                }
            }

            private SearchResponse Steer(in SearchHit hit)
            {
                if (throwOnHit)
                {
                    throw new InvalidOperationException("boom");
                }

                var text = hit.Text;
                lock (gate)
                {
                    HitTexts.Add(text);
                }

                return onHit?.Invoke(text) ?? SearchResponse.Continue;
            }

            public override void OnFileComplete(SearchFile file)
            {
                lock (gate)
                {
                    CompletedPaths.Add(file.Path);
                }
            }
        }

        [TestMethod]
        public async Task OnHit_StopFile_StopsCurrentFile_ContinuesToNext()
        {
            WriteFile("a.txt", "m m m");   // 3 matches; we stop after the first
            WriteFile("b.txt", "m m");     // still searched

            // Stop the current file on its first hit.
            var sink = new SteeringSink(onHit: _ => SearchResponse.StopFile);
            var request = Request("m", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            using var job = new SearchJob(request, sink);
            await job.RunAsync();

            Assert.AreEqual(SearchJobState.Completed, job.State);
            Assert.AreEqual(2, sink.FilePaths.Count);      // both files processed
            Assert.AreEqual(2, sink.HitTexts.Count);       // exactly one hit per file
        }

        [TestMethod]
        public async Task OnHit_StopAll_EndsTheRun()
        {
            WriteFile("a.txt", "m m m");
            WriteFile("b.txt", "m m m");

            var sink = new SteeringSink(onHit: _ => SearchResponse.StopAll);
            var request = Request("m", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            using var job = new SearchJob(request, sink);
            await job.RunAsync();

            Assert.AreEqual(SearchJobState.Canceled, job.State);
            Assert.AreEqual(1, sink.HitTexts.Count);       // stopped at the very first hit
        }

        [TestMethod]
        public async Task OnFile_StopFile_SkipsThatFilesHits()
        {
            var a = WriteFile("a.txt", "m m");

            var sink = new SteeringSink(onFile: f => f.Path == a ? SearchResponse.StopFile : SearchResponse.Continue);
            using var job = new SearchJob(Request("m", a), sink);
            await job.RunAsync();

            Assert.AreEqual(SearchJobState.Completed, job.State);
            Assert.AreEqual(1, sink.FilePaths.Count);      // OnFile was called
            Assert.AreEqual(0, sink.HitTexts.Count);       // but the file was skipped, no hits
        }

        [TestMethod]
        public async Task SinkThrows_FaultsTheJob()
        {
            WriteFile("a.txt", "m");

            var sink = new SteeringSink(throwOnHit: true);
            var request = Request("m", tempDir);
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            using var job = new SearchJob(request, sink);

            Exception? caught = null;
            try
            {
                await job.RunAsync();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.IsInstanceOfType(caught, typeof(InvalidOperationException));
            Assert.AreEqual("boom", caught!.Message);
            Assert.AreEqual(SearchJobState.Faulted, job.State);
        }
    }
}
