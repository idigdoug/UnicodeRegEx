namespace UnicodeRegEx.Gui
{
    using System;
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
    /// records its replacement so the results can later be applied). Layout is built in code for now and can
    /// be re-laid-out in the designer later.
    /// </para>
    /// </summary>
    internal sealed class CoreSettingsPane : UserControl
    {
        // The four core inputs are editable combo boxes so each can become an MRU dropdown later (persistence
        // is not wired yet — they start with an empty item list and behave like text boxes).
        private readonly ComboBox patternBox;
        private readonly ComboBox pathBox;
        private readonly ComboBox replaceBox;
        private readonly ComboBox includeFilesBox;
        private readonly CheckBox matchCaseCheck;
        private readonly CheckBox recurseCheck;
        private readonly CheckBox perlRegexCheck;
        private readonly Button searchButton;
        private readonly Button browseButton;
        private readonly Button replaceButton;
        private readonly Button cancelButton;
        private readonly Button collapseButton;

        private SearchSettings? settings;

        public CoreSettingsPane()
        {
            // Row order and labels follow the old app: Search for / Replace with / In files / In folders, each
            // paired with the button that acts on that row (Search / Replace / Cancel / Browse).
            var patternLabel = new Label { Text = "Search for:", AutoSize = true, Left = 8, Top = 12 };
            patternBox = new ComboBox { Left = 95, Top = 8, Width = 275, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDown };

            var replaceLabel = new Label { Text = "Replace with:", AutoSize = true, Left = 8, Top = 40 };
            replaceBox = new ComboBox { Left = 95, Top = 36, Width = 275, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDown };

            // Semicolon-separated include-file globs (e.g. *.cs;*.h). All entries are include filters; the
            // ordered/mixed include-exclude case is out of scope for the core page (these are WorkingState).
            var includeFilesLabel = new Label { Text = "In files:", AutoSize = true, Left = 8, Top = 68 };
            includeFilesBox = new ComboBox { Left = 95, Top = 64, Width = 275, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDown };

            var pathLabel = new Label { Text = "In folders:", AutoSize = true, Left = 8, Top = 96 };
            pathBox = new ComboBox { Left = 95, Top = 92, Width = 275, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, DropDownStyle = ComboBoxStyle.DropDown, Text = "." };

            matchCaseCheck = new CheckBox { Text = "Match case", AutoSize = true, Left = 95, Top = 124 };

            // "Search subfolders" is a three-state view over the multi-choice Directories setting: checked =
            // a recursing disposition, unchecked = ReadImmediateFiles, indeterminate = a disposition the
            // checkbox doesn't model (Error/Skip) — a push in that state leaves Directories untouched. Like the
            // Perl checkbox, AutoCheck is off so a user click only toggles checked/unchecked.
            recurseCheck = new CheckBox { Text = "Search subfolders", AutoSize = true, Left = 225, Top = 124, ThreeState = true, AutoCheck = false, CheckState = CheckState.Checked };

            // "Perl regular expression" is a three-state view over the multi-choice SyntaxFlavor setting:
            // checked = Perl, unchecked = Literal (fixed strings), indeterminate = some other flavor the
            // checkbox does not model (Basic/Extended) — in which case a push leaves SyntaxFlavor untouched.
            // AutoCheck is off so a user click can't cycle *into* indeterminate (only code sets that); the
            // Click handler below drives a clean two-way toggle.
            perlRegexCheck = new CheckBox { Text = "Perl regular expression", AutoSize = true, Left = 95, Top = 148, ThreeState = true, AutoCheck = false, CheckState = CheckState.Checked };

            // Right-hand buttons, each aligned with the input row it acts on.
            searchButton = new Button { Text = "Search", Left = 380, Top = 7, Width = 90, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            replaceButton = new Button { Text = "Replace", Left = 380, Top = 35, Width = 90, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            cancelButton = new Button { Text = "Cancel", Left = 380, Top = 63, Width = 90, Anchor = AnchorStyles.Top | AnchorStyles.Right, Enabled = false };
            browseButton = new Button { Text = "Browse...", Left = 380, Top = 91, Width = 90, Anchor = AnchorStyles.Top | AnchorStyles.Right };

            // Collapse button in the bottom-right corner: the pane owns the control; MainForm owns the swap.
            collapseButton = new Button { Text = "Hide \u25B4", Left = 380, Top = 172, Width = 90, Anchor = AnchorStyles.Top | AnchorStyles.Right };

            searchButton.Click += (_, _) => { PushToSettings(); SearchRequested?.Invoke(this, EventArgs.Empty); };
            replaceButton.Click += (_, _) => { PushToSettings(); ReplaceRequested?.Invoke(this, EventArgs.Empty); };
            cancelButton.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
            browseButton.Click += (_, _) => BrowseForPath();
            collapseButton.Click += (_, _) => { PushToSettings(); CollapseRequested?.Invoke(this, EventArgs.Empty); };

            // These two checkboxes are tri-state so code can reflect an out-of-model setting as indeterminate;
            // with AutoCheck off the state doesn't change on its own, so drive a clean two-way toggle here (a
            // user click always lands on checked or unchecked; a click on indeterminate commits to checked).
            perlRegexCheck.Click += (_, _) => ToggleTwoState(perlRegexCheck);
            recurseCheck.Click += (_, _) => ToggleTwoState(recurseCheck);

            Width = 500;
            Height = 204;

            Controls.Add(patternLabel);
            Controls.Add(patternBox);
            Controls.Add(replaceLabel);
            Controls.Add(replaceBox);
            Controls.Add(includeFilesLabel);
            Controls.Add(includeFilesBox);
            Controls.Add(pathLabel);
            Controls.Add(pathBox);
            Controls.Add(matchCaseCheck);
            Controls.Add(recurseCheck);
            Controls.Add(perlRegexCheck);
            Controls.Add(searchButton);
            Controls.Add(replaceButton);
            Controls.Add(cancelButton);
            Controls.Add(browseButton);
            Controls.Add(collapseButton);
        }

        /// <summary>Raised when the user asks to run a plain find (settings have been pushed).</summary>
        public event EventHandler? SearchRequested;

        /// <summary>Raised when the user asks to run a replacement preview (settings have been pushed).</summary>
        public event EventHandler? ReplaceRequested;

        /// <summary>Raised when the user asks to cancel a running search.</summary>
        public event EventHandler? CancelRequested;

        /// <summary>Raised when the user asks to collapse this pane (settings have been pushed).</summary>
        public event EventHandler? CollapseRequested;

        /// <summary>The button that starts a search (so <see cref="MainForm"/> can wire Enter/AcceptButton).</summary>
        public IButtonControl SearchButton => searchButton;

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
            pathBox.Text = settings.Paths.Count > 0 ? settings.Paths[0] : ".";
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
            settings.Paths.Add(pathBox.Text.Length == 0 ? "." : pathBox.Text);

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

        /// <summary>Enables/disables the inputs while a search runs (Cancel is enabled only while running).</summary>
        public void SetRunning(bool running)
        {
            searchButton.Enabled = !running;
            replaceButton.Enabled = !running;
            cancelButton.Enabled = running;
            patternBox.Enabled = !running;
            pathBox.Enabled = !running;
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
                var current = pathBox.Text.Trim();
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
                pathBox.Text = dialog.SelectedPath;
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
