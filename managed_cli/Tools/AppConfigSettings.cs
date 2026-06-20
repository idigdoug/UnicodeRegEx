namespace UnicodeRegEx.Tools
{
    using System.Collections.Generic;
    using System.Configuration;
    using UnicodeRegEx.CommandLine;

    /// <summary>
    /// Applies settings from the application's &lt;appSettings&gt; section (each key matching an
    /// option's long name) onto an option set, before the command line is parsed. This is the first
    /// configuration layer; command-line arguments override it.
    /// </summary>
    public static class AppConfigSettings
    {
        public static void Apply(OptionSet optionSet, List<string> errors)
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

            optionSet.ApplyOverlay(settings, "config", errors);
        }
    }
}
