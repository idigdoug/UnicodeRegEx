namespace UnicodeRegEx.Tools
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The persisted GUI state — most-recently-used lists for the working-state inputs and saved values for
    /// preference settings — round-tripped to an XML file. Front-end-neutral (no WinForms): a front-end binds
    /// the values to its controls. Keyed by string so the schema is stable as settings are added; a setting's
    /// key is its <see cref="Settings.Setting.LongName"/> (the established persistence key), and the plain
    /// pattern/paths inputs use the literal keys "pattern" / "paths".
    /// <para>
    /// This is a plain data model; <see cref="StateStore"/> owns the file IO (hand-written
    /// <see cref="System.Xml.XmlReader"/>/<see cref="System.Xml.XmlWriter"/>) and the MRU list semantics.
    /// </para>
    /// </summary>
    public sealed class PersistedState
    {
        /// <summary>The most-recently-used entry lists, one per working-state input, keyed by name.</summary>
        public List<MruList> MruLists { get; set; } = new List<MruList>();

        /// <summary>The saved values for preference settings, keyed by the setting's long name.</summary>
        public List<PreferenceValue> Preferences { get; set; } = new List<PreferenceValue>();

        /// <summary>The user's "open with" tools for the results context menu, in menu order.</summary>
        public List<OpenWithTool> OpenWithTools { get; set; } = new List<OpenWithTool>();

        /// <summary>Returns the MRU entries for <paramref name="key"/>, or an empty list if none.</summary>
        public IReadOnlyList<string> GetMru(string key)
        {
            var list = FindMru(key);
            return list?.Entries ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        /// <summary>
        /// Records <paramref name="value"/> as the most-recent entry for <paramref name="key"/>: moved to the
        /// top if already present (ordinal de-duplication), with the list capped at <paramref name="cap"/>
        /// entries. An empty or null value is ignored.
        /// </summary>
        public void AddMru(string key, string value, int cap)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var list = FindMru(key);
            if (list == null)
            {
                list = new MruList { Key = key, Entries = new List<string>() };
                MruLists.Add(list);
            }

            list.Entries.RemoveAll(e => string.Equals(e, value, StringComparison.Ordinal));
            list.Entries.Insert(0, value);
            if (cap > 0 && list.Entries.Count > cap)
            {
                list.Entries.RemoveRange(cap, list.Entries.Count - cap);
            }
        }

        /// <summary>Replaces the MRU entries for <paramref name="key"/> (used for seeding an empty list).</summary>
        public void SetMru(string key, IEnumerable<string> entries)
        {
            var list = FindMru(key);
            if (list == null)
            {
                list = new MruList { Key = key };
                MruLists.Add(list);
            }

            list.Entries = new List<string>(entries);
        }

        /// <summary>The saved preference value for <paramref name="key"/>, or null if none.</summary>
        public string? GetPreference(string key)
        {
            foreach (var p in Preferences)
            {
                if (string.Equals(p.Key, key, StringComparison.Ordinal))
                {
                    return p.Value;
                }
            }

            return null;
        }

        /// <summary>Sets (or adds) the saved preference value for <paramref name="key"/>.</summary>
        public void SetPreference(string key, string value)
        {
            foreach (var p in Preferences)
            {
                if (string.Equals(p.Key, key, StringComparison.Ordinal))
                {
                    p.Value = value;
                    return;
                }
            }

            Preferences.Add(new PreferenceValue { Key = key, Value = value });
        }

        /// <summary>The saved "open with" tools, in menu order (empty if none saved).</summary>
        public IReadOnlyList<OpenWithTool> GetOpenWithTools() => OpenWithTools;

        /// <summary>Replaces the "open with" tool list (used when the editor dialog commits).</summary>
        public void SetOpenWithTools(IEnumerable<OpenWithTool> tools)
        {
            OpenWithTools = new List<OpenWithTool>(tools);
        }

        private MruList? FindMru(string key)
        {
            foreach (var list in MruLists)
            {
                if (string.Equals(list.Key, key, StringComparison.Ordinal))
                {
                    return list;
                }
            }

            return null;
        }
    }

    /// <summary>One named most-recently-used list (see <see cref="PersistedState"/>).</summary>
    public sealed class MruList
    {
        public string Key { get; set; } = string.Empty;

        public List<string> Entries { get; set; } = new List<string>();
    }

    /// <summary>One saved preference value (see <see cref="PersistedState"/>).</summary>
    public sealed class PreferenceValue
    {
        public string Key { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// One "open with" tool: a display <see cref="Name"/> and a <see cref="CommandLine"/> template whose
    /// tokens (<c>$F</c> file, <c>$L</c> line, <c>$C</c> column, <c>$$</c> literal <c>$</c>) are substituted
    /// per hit before launching (see the tool-launch helper).
    /// </summary>
    public sealed class OpenWithTool
    {
        public OpenWithTool()
        {
        }

        public OpenWithTool(string name, string commandLine)
        {
            Name = name;
            CommandLine = commandLine;
        }

        public string Name { get; set; } = string.Empty;

        public string CommandLine { get; set; } = string.Empty;
    }
}
