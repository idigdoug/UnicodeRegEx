namespace UnicodeRegEx.Tests.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Collecting;
    using UnicodeRegEx.Tools.Engine;

    [TestClass]
    public class ReplaceJobTests
    {
        private string tempDir = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "urex_replacejob_" + Guid.NewGuid().ToString("N"));
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
                // Best-effort.
            }
        }

        private string WriteUtf8(string relativePath, string content)
        {
            var full = Path.Combine(tempDir, relativePath);
            File.WriteAllText(full, content, new UTF8Encoding(false));
            return full;
        }

        // Runs a Replace-mode preview and returns the captured hit records (the input to a ReplaceJob).
        private static async Task<IReadOnlyList<HitRecord>> PreviewAsync(IEnumerable<string> paths, string pattern, string template)
        {
            var request = new SearchRequest
            {
                Pattern = pattern,
                DefaultCodePage = RegExCodePage.Utf8,
                ReplaceTemplate = template,
                Verb = SearchVerb.Match,
            };
            foreach (var p in paths)
            {
                request.Paths.Add(p);
            }

            var sink = new CollectingSink(captureReplacements: true);
            using var job = new SearchJob(request, sink);
            await job.RunAsync();
            return sink.Hits;
        }

        private static async Task<ReplaceJob> ApplyAsync(IEnumerable<HitRecord> chosen)
        {
            var job = new ReplaceJob(chosen);
            await job.RunAsync();
            return job;
        }

        [TestMethod]
        public async Task Apply_AllSelected_ReplacesEveryMatch()
        {
            var file = WriteUtf8("a.txt", "foo bar foo");
            var hits = await PreviewAsync(new[] { file }, "foo", "BAZ");
            Assert.AreEqual(2, hits.Count);

            using var job = await ApplyAsync(hits);

            Assert.AreEqual("BAZ bar BAZ", File.ReadAllText(file));
            Assert.AreEqual(2, job.AppliedCount);
            Assert.AreEqual(0, job.SkippedStaleCount);
            Assert.AreEqual(1, job.ChangedFiles.Count);
            Assert.AreEqual(SearchJobState.Completed, job.State);
        }

        [TestMethod]
        public async Task Apply_SubsetSelected_ReplacesOnlyChosen()
        {
            var file = WriteUtf8("a.txt", "foo bar foo");
            var hits = await PreviewAsync(new[] { file }, "foo", "BAZ");

            var second = hits.Single(h => h.MatchFileOffset == (nuint)8);
            using var job = await ApplyAsync(new[] { second });

            Assert.AreEqual("foo bar BAZ", File.ReadAllText(file));
            Assert.AreEqual(1, job.AppliedCount);
        }

        [TestMethod]
        public async Task Apply_NoneSelected_LeavesFileUntouched()
        {
            var file = WriteUtf8("a.txt", "foo bar foo");
            await PreviewAsync(new[] { file }, "foo", "BAZ");

            using var job = await ApplyAsync(Array.Empty<HitRecord>());

            Assert.AreEqual("foo bar foo", File.ReadAllText(file));
            Assert.AreEqual(0, job.AppliedCount);
            Assert.AreEqual(0, job.ChangedFiles.Count); // ReplaceJob only rewrites files with a valid edit
        }

        [TestMethod]
        public async Task Apply_FileChangedSincePreview_SkipsStaleMatch()
        {
            var file = WriteUtf8("a.txt", "foo bar foo");
            var hits = await PreviewAsync(new[] { file }, "foo", "BAZ");
            Assert.AreEqual(2, hits.Count);

            // Edit the middle word: both "foo" matches stay at offsets 0 and 8 but their captured context
            // changes, so the staleness guard must reject both and leave the file untouched.
            WriteUtf8("a.txt", "foo BAR foo");

            using var job = await ApplyAsync(hits);

            Assert.AreEqual(0, job.AppliedCount);
            Assert.AreEqual(2, job.SkippedStaleCount);
            Assert.AreEqual(0, job.ChangedFiles.Count);
            Assert.AreEqual("foo BAR foo", File.ReadAllText(file));
        }

        [TestMethod]
        public async Task Apply_MultipleFiles_RewritesEachChosen()
        {
            var a = WriteUtf8("a.txt", "foo aaa foo");
            var b = WriteUtf8("b.txt", "foo bbb foo");
            var hits = await PreviewAsync(new[] { a, b }, "foo", "BAZ");
            Assert.AreEqual(4, hits.Count);

            using var job = await ApplyAsync(hits);

            Assert.AreEqual("BAZ aaa BAZ", File.ReadAllText(a));
            Assert.AreEqual("BAZ bbb BAZ", File.ReadAllText(b));
            Assert.AreEqual(4, job.AppliedCount);
            Assert.AreEqual(2, job.ChangedFiles.Count);
            Assert.AreEqual(2, job.TotalFileCount);
        }

        [TestMethod]
        public async Task Apply_OneFileErrors_OthersStillApplied()
        {
            var good = WriteUtf8("good.txt", "foo bar foo");
            var missing = Path.Combine(tempDir, "gone.txt");
            var alsoGood = WriteUtf8("good2.txt", "foo baz foo");

            // Preview all three (gone.txt is written, previewed, then deleted so the apply hits an error).
            var goneTmp = WriteUtf8("gone.txt", "foo qux foo");
            var hits = await PreviewAsync(new[] { good, goneTmp, alsoGood }, "foo", "BAZ");
            File.Delete(missing);

            using var job = await ApplyAsync(hits);

            // The missing file reports an error; the other two are still rewritten.
            Assert.AreEqual("BAZ bar BAZ", File.ReadAllText(good));
            Assert.AreEqual("BAZ baz BAZ", File.ReadAllText(alsoGood));
            Assert.AreEqual(4, job.AppliedCount);          // 2 per surviving file
            Assert.AreEqual(1, job.Errors.Count);
            Assert.AreEqual(missing, job.Errors[0].Path);
            Assert.AreEqual(SearchJobState.Completed, job.State);
        }
    }
}
