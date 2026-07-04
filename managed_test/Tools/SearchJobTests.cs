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
        public async Task RecurseWithLinks_FollowsJunctionedDirectory()
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

            // The file is reached both through "real" and through the followed junction "link".
            Assert.AreEqual(2, sink.Hits.Count);
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
            request.Verb = SearchVerb.Replace;
            request.ReplaceTemplate = "X";
            request.Apply = true;

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

        [TestMethod]
        public async Task ApplyReplace_NoMatch_LeavesFileUntouched()
        {
            var file = WriteFile("a.txt", "no matches here");
            var before = File.ReadAllText(file);

            var request = Request("zzz", file);
            request.Verb = SearchVerb.Replace;
            request.ReplaceTemplate = "X";
            request.Apply = true;

            var (_, summary, _) = await RunAsync(request);

            Assert.AreEqual(0, summary.FilesChanged);
            Assert.AreEqual(before, File.ReadAllText(file));
        }

        [TestMethod]
        public async Task ApplyReplace_ReportsHitPerMatch_WithReplacement()
        {
            var file = WriteFile("a.txt", "alpha beta alpha");
            var request = Request("alpha", file);
            request.Verb = SearchVerb.Replace;
            request.ReplaceTemplate = "X";
            request.Apply = true;

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
            request.Verb = SearchVerb.Replace;
            request.ReplaceTemplate = "X";
            request.Apply = true;

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
            request.Verb = SearchVerb.Replace;
            request.ReplaceTemplate = "X";
            request.Apply = true;

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
        private sealed class OffsetCapturingSink : ISearchSink
        {
            public List<(nuint Begin, nuint Size)> Spans { get; } = new List<(nuint, nuint)>();

            public SearchResponse OnFile(SearchFile file) => SearchResponse.Continue;

            public SearchResponse OnHit(in SearchHit hit)
            {
                var whole = hit.Match.GetSubMatch(0);
                Spans.Add((whole.Begin, whole.Size));
                return SearchResponse.Continue;
            }

            public void OnFileChanged(string path)
            {
            }

            public void OnError(string path, Exception exception)
            {
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
        private sealed class SteeringSink : ISearchSink
        {
            private readonly Func<string, SearchResponse>? onHit;
            private readonly Func<SearchFile, SearchResponse>? onFile;
            private readonly bool throwOnHit;

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

            public SearchResponse OnFile(SearchFile file)
            {
                FilePaths.Add(file.Path);
                return onFile?.Invoke(file) ?? SearchResponse.Continue;
            }

            public SearchResponse OnHit(in SearchHit hit)
            {
                if (throwOnHit)
                {
                    throw new InvalidOperationException("boom");
                }

                HitTexts.Add(hit.Text);
                return onHit?.Invoke(hit.Text) ?? SearchResponse.Continue;
            }

            public void OnFileChanged(string path)
            {
            }

            public void OnError(string path, Exception exception)
            {
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
