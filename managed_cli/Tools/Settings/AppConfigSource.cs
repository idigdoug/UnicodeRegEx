namespace UnicodeRegEx.Tools.Settings
{
    using System.Collections.Generic;
    using System.Configuration;

    /// <summary>
    /// Applies settings from the application's &lt;appSettings&gt; section (each key matching a
    /// setting's long name) onto a setting group, before the command line is parsed. This is the first
    /// configuration layer; command-line arguments override it.
    /// </summary>
    public static class AppConfigSource
    {
        public static void Apply(SettingGroup settingGroup, List<string> errors)
        {
            var appSettings = ConfigurationManager.AppSettings;
            var settings = new List<KeyValuePair<string, string?>>(appSettings.Count);
            foreach (string? key in appSettings.AllKeys)
            {
                if (key != null)
                {
                    settings.Add(new KeyValuePair<string, string?>(key, appSettings[key]));
                }
            }

            settingGroup.ApplyOverlay(settings, "config", errors);
        }
    }
}
