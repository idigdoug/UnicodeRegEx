namespace UnicodeRegEx.Tests.Tools
{
    using System;
    using System.IO;
    using System.Linq;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx.Tools;

    [TestClass]
    public class StateStoreTests
    {
        private string tempDir = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "urex_state_" + Guid.NewGuid().ToString("N"));
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

        // MRU semantics

        [TestMethod]
        public void AddMru_MostRecentFirst()
        {
            var s = new PersistedState();
            s.AddMru("pattern", "a", cap: 10);
            s.AddMru("pattern", "b", cap: 10);
            s.AddMru("pattern", "c", cap: 10);

            CollectionAssert.AreEqual(new[] { "c", "b", "a" }, s.GetMru("pattern").ToArray());
        }

        [TestMethod]
        public void AddMru_MovesExistingToTop_Deduplicated()
        {
            var s = new PersistedState();
            s.AddMru("pattern", "a", cap: 10);
            s.AddMru("pattern", "b", cap: 10);
            s.AddMru("pattern", "a", cap: 10); // re-use 'a'

            CollectionAssert.AreEqual(new[] { "a", "b" }, s.GetMru("pattern").ToArray());
        }

        [TestMethod]
        public void AddMru_CapsLength()
        {
            var s = new PersistedState();
            for (var i = 0; i < 15; i++)
            {
                s.AddMru("pattern", "v" + i, cap: 10);
            }

            var mru = s.GetMru("pattern");
            Assert.AreEqual(10, mru.Count);
            Assert.AreEqual("v14", mru[0]);  // most recent
            Assert.AreEqual("v5", mru[9]);   // oldest kept (v0..v4 dropped)
        }

        [TestMethod]
        public void AddMru_IgnoresEmpty()
        {
            var s = new PersistedState();
            s.AddMru("pattern", "", cap: 10);
            s.AddMru("pattern", null!, cap: 10);
            Assert.AreEqual(0, s.GetMru("pattern").Count);
        }

        [TestMethod]
        public void GetMru_UnknownKey_IsEmpty()
        {
            Assert.AreEqual(0, new PersistedState().GetMru("nope").Count);
        }

        [TestMethod]
        public void Preferences_SetAndGet()
        {
            var s = new PersistedState();
            s.SetPreference("ignore-case", "true");
            s.SetPreference("ignore-case", "false"); // overwrite
            s.SetPreference("syntax-flavor", "fixed");

            Assert.AreEqual("false", s.GetPreference("ignore-case"));
            Assert.AreEqual("fixed", s.GetPreference("syntax-flavor"));
            Assert.IsNull(s.GetPreference("missing"));
        }

        // File round-trip

        [TestMethod]
        public void SaveLoad_RoundTrips()
        {
            var path = Path.Combine(tempDir, "state.xml");
            var s = new PersistedState();
            s.AddMru("pattern", "foo", cap: 10);
            s.AddMru("pattern", "bar", cap: 10);
            s.AddMru("file-name-filters", "*.cs;*.h", cap: 10);
            s.SetPreference("syntax-flavor", "fixed");

            StateStore.Save(path, s);
            var loaded = StateStore.Load(path);

            CollectionAssert.AreEqual(new[] { "bar", "foo" }, loaded.GetMru("pattern").ToArray());
            CollectionAssert.AreEqual(new[] { "*.cs;*.h" }, loaded.GetMru("file-name-filters").ToArray());
            Assert.AreEqual("fixed", loaded.GetPreference("syntax-flavor"));
        }

        [TestMethod]
        public void Save_CreatesDirectory()
        {
            var path = Path.Combine(tempDir, "nested", "sub", "state.xml");
            StateStore.Save(path, new PersistedState());
            Assert.IsTrue(File.Exists(path));
        }

        [TestMethod]
        public void Load_MissingFile_ReturnsEmpty()
        {
            var loaded = StateStore.Load(Path.Combine(tempDir, "does-not-exist.xml"));
            Assert.AreEqual(0, loaded.MruLists.Count);
            Assert.AreEqual(0, loaded.Preferences.Count);
        }

        [TestMethod]
        public void Load_CorruptFile_ReturnsEmpty()
        {
            var path = Path.Combine(tempDir, "corrupt.xml");
            File.WriteAllText(path, "this is not xml <<<");
            var loaded = StateStore.Load(path);
            Assert.AreEqual(0, loaded.MruLists.Count);
        }
    }
}
