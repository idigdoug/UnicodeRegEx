namespace UnicodeRegEx.Tests.Tools
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
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
            File.WriteAllText(full, content, new System.Text.UTF8Encoding(false));
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
        public async Task Search_NoMatch_CompletesWithoutHits()
        {
            var file = WriteFile("a.txt", "nothing here");
            var (sink, summary, state) = await RunAsync(Request("zzz", file));

            Assert.AreEqual(SearchJobState.Completed, state);
            Assert.IsFalse(summary.AnyMatch);
            Assert.AreEqual(0, sink.Hits.Count);
        }

        [TestMethod]
        public async Task Search_Directory_NonRecursive_OnlyTopLevel()
        {
            WriteFile("top.txt", "match");
            WriteFile("sub\\nested.txt", "match");

            var request = Request("match", tempDir); // Recurse defaults to false
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
            request.Recurse = true;
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
            request.Include = "*.cs";
            var (sink, _, _) = await RunAsync(request);

            Assert.AreEqual(1, sink.Hits.Count);
        }

        [TestMethod]
        public async Task Search_NamedFile_BypassesIncludeGlob()
        {
            // An explicitly named file is always searched, even if it doesn't match --include.
            var file = WriteFile("data.txt", "match");
            var request = Request("match", file);
            request.Include = "*.cs";
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
            var before = System.Text.Encoding.ASCII.GetBytes(asciiBefore);
            var after = System.Text.Encoding.ASCII.GetBytes(asciiAfter);
            var bytes = new byte[before.Length + 1 + after.Length];
            System.Array.Copy(before, 0, bytes, 0, before.Length);
            bytes[before.Length] = 0x00; // NUL -> binary
            System.Array.Copy(after, 0, bytes, before.Length + 1, after.Length);
            File.WriteAllBytes(full, bytes);
            return full;
        }

        [TestMethod]
        public async Task BinarySkip_Default_NoHitsNoErrors()
        {
            var file = WriteBinaryFile("bin.dat", "alpha", "alpha");

            var (sink, summary, _) = await RunAsync(Request("alpha", file)); // default disposition = Skip

            Assert.AreEqual(0, sink.Hits.Count);
            Assert.AreEqual(0, sink.Errors.Count);
            Assert.IsFalse(summary.AnyMatch);
        }

        [TestMethod]
        public async Task BinaryError_ReportsErrorAndDoesNotProcess()
        {
            var file = WriteBinaryFile("bin.dat", "alpha", "alpha");
            var request = Request("alpha", file);
            request.BinaryDisposition = BinaryFileDisposition.Error;

            var (sink, summary, _) = await RunAsync(request);

            Assert.AreEqual(0, sink.Hits.Count);
            Assert.AreEqual(1, sink.Errors.Count);
            Assert.AreEqual(file, sink.Errors[0].Path);
            Assert.AreEqual("binary file", sink.Errors[0].Message);
            Assert.AreEqual(1, summary.Errors);
        }

        [TestMethod]
        public async Task BinarySearch_SearchesAnyway_FindsMatches()
        {
            var file = WriteBinaryFile("bin.dat", "alpha", "alpha");
            var request = Request("alpha", file);
            request.BinaryDisposition = BinaryFileDisposition.Search;

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
            var file = WriteBinaryFile("bin.dat", "alpha", "alpha"); // default disposition = Skip

            var (sink, _, _) = await RunAsync(Request("alpha", file));

            Assert.AreEqual(0, sink.Files.Count); // skipped files are not "processed"
            Assert.AreEqual(0, sink.Hits.Count);
        }

        [TestMethod]
        public async Task OnFile_BinarySearchedAnyway_IsReportedAsBinary()
        {
            var file = WriteBinaryFile("bin.dat", "alpha", "alpha");
            var request = Request("alpha", file);
            request.BinaryDisposition = BinaryFileDisposition.Search;

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
    }
}
