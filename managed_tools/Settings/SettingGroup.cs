namespace UnicodeRegEx.Tools.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;

    /// <summary>
    /// A collection of settings. Subclasses declare settings as public fields; they are discovered
    /// automatically (in declaration order) so adding a setting is a single edit, and every setting
    /// is available for both parsing and --help.
    /// </summary>
    public abstract class SettingGroup
    {
        private Setting[]? settings;

        public IReadOnlyList<Setting> Settings => settings ??= Collect();

        /// <summary>
        /// Applies a set of name-&gt;value settings (from a config file, environment, etc.) onto the
        /// settings in this group, matching by <see cref="Setting.LongName"/>. Run this before
        /// the command line so command-line arguments take precedence. Unknown names and unparseable
        /// values are collected in <paramref name="errors"/> (prefixed with
        /// <paramref name="sourceLabel"/>) rather than thrown.
        /// </summary>
        public void ApplyOverlay(
            IEnumerable<KeyValuePair<string, string?>> values,
            string sourceLabel,
            List<string> errors)
        {
            var byName = new Dictionary<string, Setting>(StringComparer.Ordinal);
            foreach (var setting in Settings)
            {
                byName[setting.LongName] = setting;
            }

            foreach (var value in values)
            {
                if (!byName.TryGetValue(value.Key, out var setting))
                {
                    errors.Add($"{sourceLabel}: unknown setting '{value.Key}'");
                    continue;
                }

                try
                {
                    setting.Apply(value.Value);
                }
                catch (Exception ex)
                {
                    errors.Add($"{sourceLabel}: {ex.Message}");
                }
            }
        }

        private Setting[] Collect()
        {
            var result = new List<Setting>();
            foreach (var field in GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.GetValue(this) is Setting setting)
                {
                    result.Add(setting);
                }
            }

            return result.ToArray();
        }
    }
}
