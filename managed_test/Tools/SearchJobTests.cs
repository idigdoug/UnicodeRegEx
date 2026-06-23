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
    }
}
