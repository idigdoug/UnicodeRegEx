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
                SettingRole.Preference, SettingCategory.Matching, "verbose", 'v', "Verbose output.");

            public readonly ValueSetting<int> Count = new ValueSetting<int>(
                SettingRole.WorkingState, SettingCategory.FileAndDirectorySelection, "count", null, "n", "How many.",
                defaultValue: 1, editorKind: EditorKind.Integer, parse: int.Parse);
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

        [TestMethod]
        public void GroupedSettings_GroupsByCategory_InEnumOrder_OmittingEmpty()
        {
            var group = new SampleGroup();
            var grouped = group.GroupedSettings;

            // SampleGroup has Verbose (Matching) then Count (Files); Replacement/Encoding are empty.
            Assert.AreEqual(2, grouped.Count);
            Assert.AreEqual(SettingCategory.Matching, grouped[0].Category);
            Assert.AreEqual(SettingCategory.FileAndDirectorySelection, grouped[1].Category);
            Assert.AreSame(group.Verbose, grouped[0].Settings[0]);
            Assert.AreSame(group.Count, grouped[1].Settings[0]);
        }

        [TestMethod]
        public void GroupedSettings_Title_ComesFromDisplayName()
        {
            var grouped = new SampleGroup().GroupedSettings;
            foreach (var group in grouped)
            {
                var category = group.Category;
                var displayName = SettingCategories.DisplayName(category);
                Assert.AreEqual(displayName, group.Title);
            }
        }

        [TestMethod]
        public void GroupedSettings_AreCachedAcrossCalls()
        {
            var group = new SampleGroup();
            Assert.AreSame(group.GroupedSettings, group.GroupedSettings);
        }

        [TestMethod]
        public void GroupedSettings_ContainSameSettingsAsFlatList()
        {
            var group = new SampleGroup();
            var flat = new List<Setting>(group.Settings);
            var fromGroups = new List<Setting>();
            foreach (var section in group.GroupedSettings)
            {
                fromGroups.AddRange(section.Settings);
            }

            CollectionAssert.AreEquivalent(flat, fromGroups);
        }
    }
}
