namespace UnicodeRegEx.Tools
{
    using System;
    using System.IO;
    using System.Xml;

    /// <summary>
    /// Loads and saves <see cref="PersistedState"/> to an XML file. The default location is
    /// <c>%APPDATA%\UnicodeRegEx\state.xml</c>. Loading is robust: a missing or unreadable/corrupt file yields
    /// a fresh empty state rather than throwing, so a bad file never blocks startup.
    /// <para>
    /// The document is read/written directly with <see cref="XmlReader"/>/<see cref="XmlWriter"/> — the schema
    /// is small and stable, so this avoids <c>XmlSerializer</c>'s reflection and generated temp assembly. The
    /// element/attribute names are the constants below, shared by the reader and writer so they cannot drift.
    /// </para>
    /// </summary>
    public static class StateStore
    {
        private const string RootElement = "UnicodeRegExState";
        private const string MruListsElement = "MruLists";
        private const string MruElement = "Mru";
        private const string EntriesElement = "Entries";
        private const string EntryElement = "Entry";
        private const string PreferencesElement = "Preferences";
        private const string PreferenceElement = "Preference";
        private const string OpenWithToolsElement = "OpenWithTools";
        private const string ToolElement = "Tool";
        private const string NameAttribute = "name";
        private const string CommandLineAttribute = "commandLine";
        private const string KeyAttribute = "key";
        private const string ValueAttribute = "value";

        /// <summary>The default state-file path: <c>%APPDATA%\UnicodeRegEx\state.xml</c>.</summary>
        public static string DefaultPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "UnicodeRegEx",
                "state.xml");

        /// <summary>
        /// Loads the persisted state from <paramref name="path"/>. Returns a fresh empty
        /// <see cref="PersistedState"/> if the file is missing, unreadable, or malformed.
        /// </summary>
        public static PersistedState Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new PersistedState();
                }

                var settings = new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true };
                using var reader = XmlReader.Create(path, settings);
                return ReadState(reader);
            }
            catch (Exception)
            {
                // A corrupt/unreadable state file must never block startup; start from empty.
                return new PersistedState();
            }
        }

        /// <summary>
        /// Saves <paramref name="state"/> to <paramref name="path"/>, creating the directory if needed. Throws
        /// on IO failure (a caller saving on shutdown may choose to ignore it).
        /// </summary>
        public static void Save(string path, PersistedState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new XmlWriterSettings { Indent = true };
            using var writer = XmlWriter.Create(path, settings);
            WriteState(writer, state);
        }

        private static void WriteState(XmlWriter writer, PersistedState state)
        {
            writer.WriteStartDocument();
            writer.WriteStartElement(RootElement);

            writer.WriteStartElement(MruListsElement);
            foreach (var mru in state.MruLists)
            {
                writer.WriteStartElement(MruElement);
                writer.WriteAttributeString(KeyAttribute, mru.Key);
                writer.WriteStartElement(EntriesElement);
                foreach (var entry in mru.Entries)
                {
                    writer.WriteElementString(EntryElement, entry);
                }

                writer.WriteEndElement(); // Entries
                writer.WriteEndElement(); // Mru
            }

            writer.WriteEndElement(); // MruLists

            writer.WriteStartElement(PreferencesElement);
            foreach (var pref in state.Preferences)
            {
                writer.WriteStartElement(PreferenceElement);
                writer.WriteAttributeString(KeyAttribute, pref.Key);
                writer.WriteAttributeString(ValueAttribute, pref.Value);
                writer.WriteEndElement(); // Preference
            }

            writer.WriteEndElement(); // Preferences

            writer.WriteStartElement(OpenWithToolsElement);
            foreach (var tool in state.OpenWithTools)
            {
                writer.WriteStartElement(ToolElement);
                writer.WriteAttributeString(NameAttribute, tool.Name);
                writer.WriteAttributeString(CommandLineAttribute, tool.CommandLine);
                writer.WriteEndElement(); // Tool
            }

            writer.WriteEndElement(); // OpenWithTools

            writer.WriteEndElement(); // root
            writer.WriteEndDocument();
        }

        private static PersistedState ReadState(XmlReader reader)
        {
            var state = new PersistedState();

            if (!reader.ReadToFollowing(RootElement))
            {
                return state;
            }

            // Walk the root's direct children, dispatching the two known sections and ignoring anything else.
            using var root = reader.ReadSubtree();
            root.Read(); // position on the root element
            while (root.Read())
            {
                if (root.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (root.Name == MruListsElement)
                {
                    ReadMruLists(root, state);
                }
                else if (root.Name == PreferencesElement)
                {
                    ReadPreferences(root, state);
                }
                else if (root.Name == OpenWithToolsElement)
                {
                    ReadOpenWithTools(root, state);
                }
            }

            return state;
        }

        private static void ReadMruLists(XmlReader reader, PersistedState state)
        {
            using var lists = reader.ReadSubtree();
            lists.Read();
            while (lists.Read())
            {
                if (lists.NodeType == XmlNodeType.Element && lists.Name == MruElement)
                {
                    var mru = new MruList { Key = lists.GetAttribute(KeyAttribute) ?? string.Empty };
                    ReadEntries(lists, mru);
                    state.MruLists.Add(mru);
                }
            }
        }

        private static void ReadEntries(XmlReader mruReader, MruList mru)
        {
            using var entries = mruReader.ReadSubtree();
            entries.Read();
            var advance = true;
            while (advance ? entries.Read() : !entries.EOF)
            {
                if (entries.NodeType == XmlNodeType.Element && entries.Name == EntryElement)
                {
                    // ReadElementContentAsString consumes the element and leaves the reader positioned on the
                    // NEXT node, so the loop must not call Read() again this iteration or it skips a sibling.
                    mru.Entries.Add(entries.ReadElementContentAsString());
                    advance = false;
                }
                else
                {
                    advance = true;
                }
            }
        }

        private static void ReadPreferences(XmlReader reader, PersistedState state)
        {
            using var prefs = reader.ReadSubtree();
            prefs.Read();
            while (prefs.Read())
            {
                if (prefs.NodeType == XmlNodeType.Element && prefs.Name == PreferenceElement)
                {
                    state.Preferences.Add(new PreferenceValue
                    {
                        Key = prefs.GetAttribute(KeyAttribute) ?? string.Empty,
                        Value = prefs.GetAttribute(ValueAttribute) ?? string.Empty,
                    });
                }
            }
        }

        private static void ReadOpenWithTools(XmlReader reader, PersistedState state)
        {
            using var tools = reader.ReadSubtree();
            tools.Read();
            while (tools.Read())
            {
                if (tools.NodeType == XmlNodeType.Element && tools.Name == ToolElement)
                {
                    state.OpenWithTools.Add(new OpenWithTool(
                        tools.GetAttribute(NameAttribute) ?? string.Empty,
                        tools.GetAttribute(CommandLineAttribute) ?? string.Empty));
                }
            }
        }
    }
}
