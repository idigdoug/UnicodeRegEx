namespace UnicodeRegEx.Gui
{
    using System;
    using System.Collections.Generic;
    using System.Windows.Forms;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;

    /// <summary>
    /// The collapsed form of the search settings: a single-line summary of the current
    /// <see cref="SearchSettings"/> that gives the results/details the screen while preserving a
    /// glance-back at what was searched. Clicking the summary (or the expand button) raises
    /// <see cref="ExpandRequested"/>; <see cref="MainForm"/> owns swapping back to the editor.
    /// <para>
    /// The controls and layout live in <c>CollapsedSettingsPane.Designer.cs</c> (VS-designer-owned); this file
    /// holds the behavior — the expand event, settings binding, and the summary rendering.
    /// </para>
    /// </summary>
    internal sealed partial class CollapsedSettingsPane : UserControl
    {
        private SearchSettings? settings;

        public CollapsedSettingsPane()
        {
            InitializeComponent();
        }

        /// <summary>Raised when the user asks to expand back to the full settings editor.</summary>
        public event EventHandler? ExpandRequested;

        private void expandButton_Click(object sender, EventArgs e)
        {
            ExpandRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Binds the shared settings this pane summarizes, and refreshes the summary line.</summary>
        public void Bind(SearchSettings sharedSettings)
        {
            settings = sharedSettings;
            UpdateSummary();
        }

        /// <summary>Refreshes the one-line summary from the bound settings.</summary>
        public void UpdateSummary()
        {
            if (settings == null)
            {
                summaryLabel.Text = string.Empty;
                return;
            }

            var pattern = settings.Pattern.Length == 0 ? "(no pattern)" : settings.Pattern;
            var path = settings.Paths.Count > 0 ? settings.Paths[0] : ".";

            // A compact hint of the active, non-default options so the collapsed line still conveys the shape
            // of the search. Only options that differ from their default are shown.
            var hints = new List<string>();
            var files = settings.FileNameFilters.ToDisplayString();
            if (files.Length > 0)
            {
                hints.Add("files: " + files);
            }

            if (settings.Replace.Value.Length > 0)
            {
                hints.Add("replace \u2192 " + settings.Replace.Value);
            }

            if (!settings.IgnoreCase.Value)
            {
                hints.Add("match case");
            }

            if (settings.SyntaxFlavor.Value == RegExSyntaxFlags.Literal)
            {
                hints.Add("literal");
            }

            if (settings.Directories.Value == DirectoryDisposition.ReadImmediateFiles)
            {
                hints.Add("no subfolders");
            }

            var hint = hints.Count > 0 ? "   [" + string.Join(", ", hints) + "]" : string.Empty;
            summaryLabel.Text = $"{pattern}   in   {path}{hint}";
        }
    }
}
