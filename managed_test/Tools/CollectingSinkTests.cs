namespace UnicodeRegEx.Tests.Tools
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Collecting;
    using UnicodeRegEx.Tools.Engine;

    [TestClass]
    public class CollectingSinkTests
    {
        private string tempDir = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "urex_collect_" + Guid.NewGuid().ToString("N"));
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

        private string WriteBytes(string relativePath, byte[] content)
        {
            var full = Path.Combine(tempDir, relativePath);
            File.WriteAllBytes(full, content);
            return full;
        }

        private static SearchRequest Request(string pattern, string path, string replaceTemplate = "")
        {
            var request = new SearchRequest
            {
                Pattern = pattern,
                DefaultCodePage = RegExCodePage.Utf8,
                ReplaceTemplate = replaceTemplate,
            };
            request.Paths.Add(path);
            return request;
        }

        private static async Task<CollectingSink> RunAsync(SearchRequest request)
        {
            var sink = new CollectingSink();
            using var job = new SearchJob(request, sink);
            await job.RunAsync();
            return sink;
        }

        [TestMethod]
        public async Task Collects_MatchOffsetAndBytes()
        {
            var file = WriteUtf8("a.txt", "xx foo yy");
            var sink = await RunAsync(Request("foo", file));

            Assert.AreEqual(1, sink.Hits.Count);
            var hit = sink.Hits[0];
            Assert.AreEqual(file, hit.File.Path);
            Assert.AreEqual((nuint)3, hit.MatchFileOffset); // "xx " is 3 bytes
            Assert.AreEqual("foo", hit.MatchText);
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("foo"), hit.MatchBytes);
        }

        [TestMethod]
        public async Task Context_MidFile_HasBothSides()
        {
            var file = WriteUtf8("a.txt", "before-foo-after");
            var sink = await RunAsync(Request("foo", file));

            var hit = sink.Hits[0];
            Assert.AreEqual("before-", hit.PreMatchText);
            Assert.AreEqual("-after", hit.PostMatchText);
        }

        [TestMethod]
        public async Task Context_MatchAtStart_HasEmptyPre()
        {
            var file = WriteUtf8("a.txt", "foo tail");
            var sink = await RunAsync(Request("foo", file));

            var hit = sink.Hits[0];
            Assert.AreEqual(0, hit.PreMatchBytes.Length);
            Assert.AreEqual(" tail", hit.PostMatchText);
        }

        [TestMethod]
        public async Task Context_MatchAtEnd_HasEmptyPost()
        {
            var file = WriteUtf8("a.txt", "head foo");
            var sink = await RunAsync(Request("foo", file));

            var hit = sink.Hits[0];
            Assert.AreEqual("head ", hit.PreMatchText);
            Assert.AreEqual(0, hit.PostMatchBytes.Length);
        }

        [TestMethod]
        public async Task Context_IsClampedTo64Bytes()
        {
            // 100 'a' before and after the match; the window is bounded at 64 bytes each side.
            var content = new string('a', 100) + "foo" + new string('a', 100);
            var file = WriteUtf8("a.txt", content);
            var sink = await RunAsync(Request("foo", file));

            var hit = sink.Hits[0];
            Assert.AreEqual(64, hit.PreMatchBytes.Length);
            Assert.AreEqual(64, hit.PostMatchBytes.Length);
        }

        [TestMethod]
        public async Task Utf8_MultiByte_DecodesCorrectly()
        {
            // 'é' is 2 bytes in UTF-8; ensure offsets are byte-based and text decodes correctly.
            var file = WriteUtf8("a.txt", "café foo");
            var sink = await RunAsync(Request("foo", file));

            var hit = sink.Hits[0];
            Assert.AreEqual("café ", hit.PreMatchText);
            // "café " is 6 bytes in UTF-8 (c a f é[2] space).
            Assert.AreEqual((nuint)6, hit.MatchFileOffset);
        }

        [TestMethod]
        public async Task Latin1_DecodesWithFileCodePage()
        {
            // 0xE9 is 'é' in Latin-1; write raw bytes and search as Latin-1.
            var bytes = Encoding.GetEncoding(RegExCodePage.Latin1).GetBytes("caf\u00E9 foo");
            var file = WriteBytes("a.txt", bytes);
            var request = Request("foo", file);
            request.DefaultCodePage = RegExCodePage.Latin1;

            var sink = await RunAsync(request);
            var hit = sink.Hits[0];
            Assert.AreEqual(RegExCodePage.Latin1, hit.File.CodePage);
            Assert.AreEqual("caf\u00E9 ", hit.PreMatchText);
        }

        [TestMethod]
        public async Task Replacement_FormatsWithTemplate()
        {
            var file = WriteUtf8("a.txt", "foo bar foo");
            var sink = await RunAsync(Request("foo", file, replaceTemplate: "BAZ"));

            Assert.AreEqual(2, sink.Hits.Count);
            foreach (var hit in sink.Hits)
            {
                Assert.AreEqual("BAZ", hit.ReplacementText);
                CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("BAZ"), hit.ReplacementBytes);
            }
        }

        [TestMethod]
        public async Task Replacement_WithBackreference()
        {
            var file = WriteUtf8("a.txt", "abc");
            var sink = await RunAsync(Request("(b)", file, replaceTemplate: "[$1]"));

            Assert.AreEqual("[b]", sink.Hits[0].ReplacementText);
        }

        [TestMethod]
        public async Task MultipleHits_OneFile_ReuseStream_IndependentReplacements()
        {
            var file = WriteUtf8("a.txt", "a1 a2 a3");
            var sink = await RunAsync(Request("a(.)", file, replaceTemplate: "<$1>"));

            Assert.AreEqual(3, sink.Hits.Count);
            Assert.AreEqual("<1>", sink.Hits[0].ReplacementText);
            Assert.AreEqual("<2>", sink.Hits[1].ReplacementText);
            Assert.AreEqual("<3>", sink.Hits[2].ReplacementText);
        }

        [TestMethod]
        public async Task Error_MissingPath_IsCollected()
        {
            var missing = Path.Combine(tempDir, "does-not-exist.txt");
            var sink = await RunAsync(Request("x", missing));

            Assert.AreEqual(1, sink.Errors.Count);
            Assert.AreEqual(missing, sink.Errors[0].Path);
        }
    }
}
