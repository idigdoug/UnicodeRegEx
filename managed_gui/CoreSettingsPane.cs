namespace UnicodeRegEx.Gui
{
    using System;
    using System.Collections.Generic;
    using System.Windows.Forms;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;

    /// <summary>
    /// The expanded search-settings editor. Hosts the core inputs — "Search for" (pattern), "Replace with"
    /// (template), "In files" (include globs), "In folders" (path), plus the match-case / subfolders /
    /// Perl-regex checkboxes — and the Search/Replace/Cancel/Browse/collapse buttons. Edits a shared
    /// <see cref="SearchSettings"/> (the single source of truth, set via <see cref="Bind"/>); the pane only
    /// raises intent events — <see cref="MainForm"/> owns what happens (running a find or replace, swapping to
    /// the collapsed pane).
    /// <para>
    /// Search and Replace are verbs, not settings: both run the engine's <c>Match</c> verb (neither edits
    /// files here). <see cref="SearchRequested"/> is a plain find (the replacement template is ignored);
    /// <see cref="ReplaceRequested"/> asks for a replacement preview (the template is honored and each hit
    /// records its replacement so the results can later be applied).
    /// </para>
    /// <para>
    /// The controls and layout live in <c>CoreSettingsPane.Designer.cs</c> (VS-designer-owned); this file holds
    /// the behavior — intent events, settings binding, and the button/checkbox handlers.
    /// </para>
    /// </summary>
    internal sealed partial class CoreSettingsPane : UserControl
    {
        // MRU keys for the four working-state combo boxes. The plain pattern/paths inputs use literal keys;
        // the Setting-backed inputs use their LongName so the key matches the persistence convention. Shared
        // with MainForm (which owns the StateStore) so both agree on the key strings.
        public const string PatternKey = "pattern";
        public const string PathsKey = "paths";
        public const string ReplaceKey = "replace";              // == Replace.LongName
        public const string FileFiltersKey = "file-name-filters"; // == FileNameFilters.LongName

        private SearchSettings? settings;

        public CoreSettingsPane()
        {
            InitializeComponent();
        }

        /// <summary>Raised when the user asks to run a plain find (settings have been pushed).</summary>
        public event EventHandler? SearchRequested;

        /// <summary>Raised when the user asks to run a replacement preview (settings have been pushed).</summary>
        public event EventHandler? ReplaceRequested;

        /// <summary>Raised when the user asks to collapse this pane (settings have been pushed).</summary>
        public event EventHandler? CollapseRequested;

        /// <summary>Raised when the user opens the advanced options dialog (settings have been pushed first).</summary>
        public event EventHandler? AdvancedRequested;

        /// <summary>The button that starts a search (so <see cref="MainForm"/> can wire Enter/AcceptButton).</summary>
        public IButtonControl SearchButton => searchButton;

        /// <summary>
        /// Moves focus to the collapse ("Hide") button. <see cref="MainForm"/> calls this after expanding this
        /// pane so focus lands on the toggle that got us here (symmetric with the collapsed pane's Edit button).
        /// </summary>
        public void FocusCollapseButton() => collapseButton.Focus();

        /// <summary>
        /// Populates the dropdown items of the combo box identified by <paramref name="key"/> (one of the
        /// public *Key constants) from its most-recently-used list. Leaves the box's current text unchanged.
        /// Unknown keys are ignored.
        /// </summary>
        public void SetMruItems(string key, IReadOnlyList<string> items)
        {
            var combo = ComboForKey(key);
            if (combo == null)
            {
                return;
            }

            var text = combo.Text;
            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                foreach (var item in items)
                {
                    combo.Items.Add(item);
                }
            }
            finally
            {
                combo.EndUpdate();
            }

            combo.Text = text; // Items.Clear can disturb the edit text; restore it.
        }

        private ComboBox? ComboForKey(string key)
        {
            switch (key)
            {
                case PatternKey: return patternBox;
                case PathsKey: return pathsBox;
                case ReplaceKey: return replaceBox;
                case FileFiltersKey: return includeFilesBox;
                default: return null;
            }
        }

        #region Event handlers

        private void searchButton_Click(object sender, EventArgs e)
        {
            PushToSettings();
            SearchRequested?.Invoke(this, EventArgs.Empty);
        }

        private void replaceButton_Click(object sender, EventArgs e)
        {
            PushToSettings();
            ReplaceRequested?.Invoke(this, EventArgs.Empty);
        }

        private void browseButton_Click(object sender, EventArgs e)
        {
            BrowseForPath();
        }

        private void collapseButton_Click(object sender, EventArgs e)
        {
            PushToSettings();
            CollapseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void advancedButton_Click(object sender, EventArgs e)
        {
            // Flush the primary-pane controls into settings first so the advanced dialog snapshots and edits
            // the same up-to-date values; MainForm re-pulls the controls when the dialog closes.
            PushToSettings();
            AdvancedRequested?.Invoke(this, EventArgs.Empty);
        }

        // These two checkboxes are tri-state so code can reflect an out-of-model setting as indeterminate; with
        // AutoCheck off the state doesn't change on its own, so drive a clean two-way toggle here (a user click
        // always lands on checked or unchecked; a click on indeterminate commits to checked).
        private void perlRegexCheck_Click(object sender, EventArgs e)
        {
            ToggleTwoState(perlRegexCheck);
        }

        private void recurseCheck_Click(object sender, EventArgs e)
        {
            ToggleTwoState(recurseCheck);
        }

        #endregion

        /// <summary>Binds the shared settings this pane edits, and loads the controls from it.</summary>
        public void Bind(SearchSettings sharedSettings)
        {
            settings = sharedSettings;
            PullFromSettings();
        }

        /// <summary>Loads the control values from the bound settings (call when re-showing the pane).</summary>
        public void PullFromSettings()
        {
            if (settings == null)
            {
                return;
            }

            patternBox.Text = settings.Pattern;
            pathsBox.Text = settings.Paths.Count > 0 ? settings.Paths[0] : ".";
            replaceBox.Text = settings.Replace.Value;
            includeFilesBox.Text = settings.FileNameFilters.ToDisplayString();
            matchCaseCheck.Checked = !settings.IgnoreCase.Value;
            recurseCheck.CheckState = DirectoriesToCheckState(settings.Directories.Value);
            perlRegexCheck.CheckState = SyntaxFlavorToCheckState(settings.SyntaxFlavor.Value);
        }

        /// <summary>Writes the control values back into the bound settings.</summary>
        public void PushToSettings()
        {
            if (settings == null)
            {
                return;
            }

            settings.Pattern = patternBox.Text;
            settings.Paths.Clear();
            settings.Paths.Add(pathsBox.Text.Length == 0 ? "." : pathsBox.Text);

            // Rebuild the file-name filters from the semicolon list as all-include globs (the core page only
            // offers include-file globs; empty entries are ignored).
            settings.FileNameFilters.Filters.Clear();
            foreach (var glob in includeFilesBox.Text.Split(';'))
            {
                var trimmed = glob.Trim();
                if (trimmed.Length != 0)
                {
                    settings.FileNameFilters.Filters.Add(new GlobFilter(FilterKind.Include, trimmed));
                }
            }

            // These settings' Value setters are private; go through TrySetValue (inputs are already valid
            // types, so this cannot fail here).
            settings.Replace.TrySetValue(replaceBox.Text, out _);
            settings.IgnoreCase.TrySetValue(!matchCaseCheck.Checked, out _);

            // Indeterminate means "a flavor this checkbox doesn't model" — leave SyntaxFlavor untouched.
            if (perlRegexCheck.CheckState != CheckState.Indeterminate)
            {
                settings.SyntaxFlavor.TrySetValue(
                    perlRegexCheck.CheckState == CheckState.Checked ? RegExSyntaxFlags.Perl : RegExSyntaxFlags.Literal,
                    out _);
            }

            // Same tri-state rule for Directories. Indeterminate (Error/Skip) leaves it untouched. When
            // checked, keep an already-recursing value as-is (so RecurseWithLinks set elsewhere isn't
            // downgraded) and otherwise default to RecurseNoLinks; unchecked means non-recursing.
            if (recurseCheck.CheckState == CheckState.Checked)
            {
                if (!IsRecursing(settings.Directories.Value))
                {
                    settings.Directories.TrySetValue(DirectoryDisposition.RecurseNoLinks, out _);
                }
            }
            else if (recurseCheck.CheckState == CheckState.Unchecked)
            {
                settings.Directories.TrySetValue(DirectoryDisposition.ReadImmediateFiles, out _);
            }
        }

        /// <summary>Enables/disables the settings inputs and run verbs while a search runs.</summary>
        public void SetRunning(bool running)
        {
            searchButton.Enabled = !running;
            replaceButton.Enabled = !running;
            patternBox.Enabled = !running;
            pathsBox.Enabled = !running;
            browseButton.Enabled = !running;
            replaceBox.Enabled = !running;
            includeFilesBox.Enabled = !running;
            matchCaseCheck.Enabled = !running;
            recurseCheck.Enabled = !running;
            perlRegexCheck.Enabled = !running;
            collapseButton.Enabled = !running;
        }

        // Opens a folder picker for the Path box, seeded with the current path when it names an existing
        // directory; on OK the chosen folder replaces the box's text.
        private void BrowseForPath()
        {
            using var dialog = new FolderBrowserDialog();

            try
            {
                var current = pathsBox.Text.Trim();
                if (current.Length != 0 && System.IO.Directory.Exists(current))
                {
                    dialog.SelectedPath = System.IO.Path.GetFullPath(current);
                }
            }
            catch (Exception)
            {
                // A malformed current path just means no seed; ignore.
            }

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                pathsBox.Text = dialog.SelectedPath;
            }
        }

        // True when a disposition is one of the recursing kinds (either reads as a checked "Search subfolders").
        private static bool IsRecursing(DirectoryDisposition disposition) =>
            disposition == DirectoryDisposition.RecurseNoLinks || disposition == DirectoryDisposition.RecurseWithLinks;

        // The "Perl regular expression" checkbox maps SyntaxFlavor to a tri-state: Perl -> checked, Literal ->
        // unchecked, anything else (Basic/Extended) -> indeterminate (a flavor the checkbox does not model).
        private static CheckState SyntaxFlavorToCheckState(RegExSyntaxFlags flavor)
        {
            if (flavor == RegExSyntaxFlags.Perl)
            {
                return CheckState.Checked;
            }

            if (flavor == RegExSyntaxFlags.Literal)
            {
                return CheckState.Unchecked;
            }

            return CheckState.Indeterminate;
        }

        // The "Search subfolders" checkbox maps Directories to a tri-state: a recursing disposition -> checked,
        // ReadImmediateFiles -> unchecked, anything else (Error/Skip) -> indeterminate (not modeled here).
        private static CheckState DirectoriesToCheckState(DirectoryDisposition disposition)
        {
            if (IsRecursing(disposition))
            {
                return CheckState.Checked;
            }

            if (disposition == DirectoryDisposition.ReadImmediateFiles)
            {
                return CheckState.Unchecked;
            }

            return CheckState.Indeterminate;
        }

        // A user click on a tri-state checkbox (AutoCheck off) toggles only between checked and unchecked; a
        // click while indeterminate commits to checked.
        private static void ToggleTwoState(CheckBox check) =>
            check.CheckState = check.CheckState == CheckState.Checked ? CheckState.Unchecked : CheckState.Checked;
    }
}
