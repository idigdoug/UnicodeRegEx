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
        private SettingCategoryView[]? groupedSettings;

        public IReadOnlyList<Setting> Settings => settings ??= Collect();

        /// <summary>
        /// The settings grouped into titled sections for presentation (help, GUI property page), ordered
        /// by <see cref="SettingCategory"/> (enum order); settings within a section keep declaration
        /// order. Categories with no settings are omitted.
        /// </summary>
        public IReadOnlyList<SettingCategoryView> GroupedSettings => groupedSettings ??= GroupByCategory();

        private SettingCategoryView[] GroupByCategory()
        {
            // Bucket by category preserving each category's declaration order, then emit categories in
            // enum order (skipping empties).
            var byCategory = new Dictionary<SettingCategory, List<Setting>>();
            foreach (var setting in Settings)
            {
                if (!byCategory.TryGetValue(setting.Category, out var list))
                {
                    list = new List<Setting>();
                    byCategory[setting.Category] = list;
                }

                list.Add(setting);
            }

            var views = new List<SettingCategoryView>();
            foreach (SettingCategory category in Enum.GetValues(typeof(SettingCategory)))
            {
                if (byCategory.TryGetValue(category, out var list))
                {
                    views.Add(new SettingCategoryView(category, list));
                }
            }

            return views.ToArray();
        }

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
                    setting.Apply(value.Value, setting.DefaultBinding);
                }
                catch (Exception ex)
                {
                    errors.Add($"{sourceLabel}: {ex.Message}");
                }
            }
        }

        private Setting[] Collect()
        {
            // Order by metadata token so the flat list is deterministic (reflection field order is not
            // guaranteed by the CLR); this token ordering closely tracks declaration order.
            var fields = new List<FieldInfo>(GetType().GetFields(BindingFlags.Public | BindingFlags.Instance));
            fields.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

            var result = new List<Setting>();
            foreach (var field in fields)
            {
                if (field.GetValue(this) is Setting setting)
                {
                    result.Add(setting);
                }
            }

            return result.ToArray();
        }
    }

    /// <summary>
    /// A titled section of settings for presentation (help sections, GUI property-page groups): a
    /// <see cref="SettingCategory"/> and the settings under it, in declaration order.
    /// </summary>
    public readonly struct SettingCategoryView
    {
        public SettingCategoryView(SettingCategory category, IReadOnlyList<Setting> settings)
        {
            Category = category;
            Settings = settings;
        }

        /// <summary>The category this section represents.</summary>
        public SettingCategory Category { get; }

        /// <summary>The section title (from <see cref="SettingCategories.DisplayName"/>).</summary>
        public string Title => SettingCategories.DisplayName(Category);

        /// <summary>The settings in this category, in declaration order.</summary>
        public IReadOnlyList<Setting> Settings { get; }
    }
}
