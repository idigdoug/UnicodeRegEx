namespace UnicodeRegEx.Tests.Tools
{
    using System;
    using System.Collections.Generic;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx.Tools.Settings;

    [TestClass]
    public class SettingGroupTests
    {
        // A minimal group exercising the mechanism without depending on SearchSettings' real options.
        private sealed class SampleGroup : SettingGroup
        {
            public readonly FlagSetting Verbose = new FlagSetting(
                SettingRole.Preference, "verbose", 'v', "Verbose output.");

            public readonly ValueSetting<int> Count = new ValueSetting<int>(
                SettingRole.WorkingState, "count", null, "n", "How many.",
                defaultValue: 1, parse: int.Parse);
        }

        private static List<KeyValuePair<string, string?>> Overlay(params (string Key, string? Value)[] pairs)
        {
            var list = new List<KeyValuePair<string, string?>>();
            foreach (var (key, value) in pairs)
            {
                list.Add(new KeyValuePair<string, string?>(key, value));
            }

            return list;
        }

        [TestMethod]
        public void Settings_DiscoversPublicFields_InDeclarationOrder()
        {
            var group = new SampleGroup();
            var names = new List<string>();
            foreach (var setting in group.Settings)
            {
                names.Add(setting.LongName);
            }

            CollectionAssert.AreEqual(new List<string> { "verbose", "count" }, names);
        }

        [TestMethod]
        public void ApplyOverlay_MatchesByLongName_AndAppliesValues()
        {
            var group = new SampleGroup();
            var errors = new List<string>();

            group.ApplyOverlay(Overlay(("verbose", "true"), ("count", "5")), "config", errors);

            CollectionAssert.AreEqual(new List<string>(), errors);
            Assert.IsTrue(group.Verbose.Value);
            Assert.AreEqual(5, group.Count.Value);
        }

        [TestMethod]
        public void ApplyOverlay_UnknownKey_ReportsErrorWithSourceLabel()
        {
            var group = new SampleGroup();
            var errors = new List<string>();

            group.ApplyOverlay(Overlay(("bogus", "x")), "config", errors);

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "config");
            StringAssert.Contains(errors[0], "bogus");
        }

        [TestMethod]
        public void ApplyOverlay_BadValue_ReportsErrorWithSourceLabel_AndKeepsDefault()
        {
            var group = new SampleGroup();
            var errors = new List<string>();

            group.ApplyOverlay(Overlay(("count", "not-an-int")), "env", errors);

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains(errors[0], "env");
            Assert.AreEqual(1, group.Count.Value); // unchanged
        }

        [TestMethod]
        public void ApplyOverlay_AggregatesMultipleErrors()
        {
            var group = new SampleGroup();
            var errors = new List<string>();

            group.ApplyOverlay(Overlay(("bogus", "x"), ("count", "nope")), "config", errors);

            Assert.AreEqual(2, errors.Count);
        }

        [TestMethod]
        public void ApplyOverlay_ValidValueAfterBadKey_StillApplies()
        {
            var group = new SampleGroup();
            var errors = new List<string>();

            // An error on one key must not abort processing of the rest.
            group.ApplyOverlay(Overlay(("bogus", "x"), ("count", "9")), "config", errors);

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(9, group.Count.Value);
        }

        [TestMethod]
        public void ApplyOverlay_LaterValueOverridesEarlier()
        {
            var group = new SampleGroup();
            var errors = new List<string>();

            group.ApplyOverlay(Overlay(("count", "3"), ("count", "7")), "config", errors);

            CollectionAssert.AreEqual(new List<string>(), errors);
            Assert.AreEqual(7, group.Count.Value);
        }

        [TestMethod]
        public void Settings_AreCachedAcrossCalls()
        {
            var group = new SampleGroup();
            Assert.AreSame(group.Settings, group.Settings);
        }
    }
}
