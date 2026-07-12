namespace UnicodeRegEx.Tests.Tools
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;

    [TestClass]
    public class SearchSettingsSnapshotTests
    {
        [TestMethod]
        public void Snapshot_ThenEdit_ThenRestore_RevertsPreferences()
        {
            var settings = new SearchSettings();
            var originalIgnoreCase = settings.IgnoreCase.Value;

            var snapshot = settings.SnapshotPreferences();

            // Edit a couple of preferences away from their snapshot.
            settings.IgnoreCase.TrySetValue(!originalIgnoreCase, out _);
            settings.SyntaxFlavor.TrySetValue(RegExSyntaxFlags.Literal, out _);

            settings.RestorePreferences(snapshot);

            Assert.AreEqual(originalIgnoreCase, settings.IgnoreCase.Value);
            Assert.IsTrue(settings.SyntaxFlavor.IsDefault);
        }

        [TestMethod]
        public void Snapshot_CapturesEveryPreference()
        {
            var settings = new SearchSettings();
            var snapshot = settings.SnapshotPreferences();

            foreach (var setting in settings.Settings)
            {
                if (setting.Role == UnicodeRegEx.Tools.Settings.SettingRole.Preference && !(setting is GlobListSetting))
                {
                    Assert.IsTrue(snapshot.ContainsKey(setting.LongName), $"missing {setting.LongName}");
                }
            }
        }

        [TestMethod]
        public void Restore_KeepsCommittedEdits_WhenSnapshotTakenAfter()
        {
            var settings = new SearchSettings();
            settings.IgnoreCase.TrySetValue(true, out _);

            // Snapshot the edited state; a later edit + restore should return to the edited (not default) value.
            var snapshot = settings.SnapshotPreferences();
            settings.IgnoreCase.TrySetValue(false, out _);
            settings.RestorePreferences(snapshot);

            Assert.AreEqual(true, settings.IgnoreCase.Value);
        }
    }
}
