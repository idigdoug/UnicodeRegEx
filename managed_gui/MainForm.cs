namespace UnicodeRegEx.Gui
{
    using System;
    using System.Drawing;
    using System.Threading.Tasks;
    using System.Windows.Forms;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Collecting;
    using UnicodeRegEx.Tools.Engine;

    internal partial class MainForm : Form
    {
        // How many of the sink's hits/errors have already been appended as rows. The hit and error records
        // themselves live in the sink (and on each row's Tag); these are just append cursors. UI thread only.
        private int hitsShownCount;
        private int errorsShownCount;

        // The single source of truth for the search; both panes edit this same instance by reference.
        private readonly SearchSettings settings = new SearchSettings();

        private readonly CoreSettingsPane corePane = new CoreSettingsPane();
        private readonly CollapsedSettingsPane collapsedPane = new CollapsedSettingsPane();
        private readonly ActionBar actionBar = new ActionBar();

        private CollectingSink? sink;
        private SearchJob? job;

        // The verb of the most recent run: true if it was a Replace (results carry replacements and can be
        // applied), false if a plain Find. Consulted by the results UI (selective apply arrives in slice 3).
        private bool lastRunWasReplace;

        public MainForm()
        {
            InitializeComponent();

            // Default to recursive search while the directory control lives on the (future) advanced page.
            settings.Directories.TrySetValue(DirectoryDisposition.RecurseNoLinks, out _);

            // Both panes edit the same settings object; each fills its controls from it.
            corePane.Bind(settings);
            collapsedPane.Bind(settings);

            corePane.SearchRequested += OnSearchRequested;
            corePane.ReplaceRequested += OnReplaceRequested;
            corePane.CollapseRequested += OnCollapseRequested;
            collapsedPane.ExpandRequested += OnExpandRequested;

            // The action bar owns the operation/results verbs (Cancel, Apply, Select All/None) and progress;
            // MainForm owns all the logic, same as it does for the panes.
            actionBar.CancelRequested += OnCancelRequested;
            actionBar.ApplyRequested += OnApplyRequested;
            actionBar.SelectAllRequested += OnSelectAllRequested;
            actionBar.SelectNoneRequested += OnSelectNoneRequested;
            actionBar.Dock = DockStyle.Fill;
            actionBarHost.Controls.Add(actionBar);

            // Start expanded; MainForm owns swapping the active pane in and out of the host panel.
            ShowPane(corePane);
            UpdateRunUiState(running: false);
        }

        #region Event handlers

        private void MainForm_Load(object sender, EventArgs e)
        {
        }

        private async void OnSearchRequested(object? sender, EventArgs e)
        {
            await StartRunAsync(replace: false);
        }

        private async void OnReplaceRequested(object? sender, EventArgs e)
        {
            await StartRunAsync(replace: true);
        }

        private void OnCancelRequested(object? sender, EventArgs e)
        {
            job?.Cancel();
        }

        // Results verbs — active only when a Replace run has produced checkable rows (slice 3 wires the
        // per-row checkboxes and the actual apply pass; these are reserved no-ops until then).
        private void OnApplyRequested(object? sender, EventArgs e)
        {
        }

        private void OnSelectAllRequested(object? sender, EventArgs e)
        {
        }

        private void OnSelectNoneRequested(object? sender, EventArgs e)
        {
        }

        private void OnCollapseRequested(object? sender, EventArgs e)
        {
            collapsedPane.UpdateSummary();
            ShowPane(collapsedPane);
        }

        private void OnExpandRequested(object? sender, EventArgs e)
        {
            corePane.PullFromSettings();
            ShowPane(corePane);
        }

        private void hitList_SelectedIndexChanged(object sender, EventArgs e)
        {
            ShowSelectedContext();
        }

        #endregion

        #region Helpers

        private async Task StartRunAsync(bool replace)
        {
            if (job != null)
            {
                return; // a search is already running
            }

            // The pane already pushed its controls into settings before raising the run request.
            if (settings.Pattern.Length == 0)
            {
                statusLabel.Text = "Enter a pattern.";
                return;
            }

            // Reset the results.
            hitList.BeginUpdate();
            hitList.Items.Clear();
            hitList.EndUpdate();
            hitsShownCount = 0;
            errorsShownCount = 0;
            contextBox.Clear();

            // Validate up front so an invalid pattern is a friendly message, not a faulted run.
            var problems = settings.Validate();
            if (problems.Count > 0)
            {
                statusLabel.Text = "Error: " + problems[0].Message;
                return;
            }

            // Find and Replace both run the engine's Match verb (neither edits files here); the only
            // difference is whether each hit records its replacement, so the results can later be applied.
            var request = settings.MakeRequest();
            lastRunWasReplace = replace;

            sink = new CollectingSink(captureReplacements: replace);
            sink.HitsAdded += OnHitsAdded;
            sink.ErrorsAdded += OnErrorsAdded;
            job = new SearchJob(request, sink);
            job.ProgressChanged += OnProgressChanged;

            UpdateRunUiState(running: true);
            statusLabel.Text = replace ? "Finding replacements..." : "Searching...";

            try
            {
                await job.RunAsync();

                // Both hits and errors stream in during the run (throttled / immediate); flush any tail left
                // after the last event so the final counts are exact.
                AppendNewHits();
                AppendNewErrors();
                var summary = job.Summary;
                var errorCount = sink.Errors.Count;
                statusLabel.Text = summary.Cancelled
                    ? $"Cancelled. {hitsShownCount} hit(s)."
                    : $"Done. {hitsShownCount} hit(s){(errorCount > 0 ? $", {errorCount} error(s)" : string.Empty)}.";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Error: " + ex.Message;
            }
            finally
            {
                sink.HitsAdded -= OnHitsAdded;
                sink.ErrorsAdded -= OnErrorsAdded;
                job.ProgressChanged -= OnProgressChanged;
                job.Dispose();
                job = null;
                sink = null;
                UpdateRunUiState(running: false);
            }
        }

        // Fired on a worker thread by the sink; marshal to the UI thread to append.
        private void OnHitsAdded(object? sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                BeginInvoke((Action)AppendNewHits);
            }
            catch (InvalidOperationException)
            {
                // Handle not created yet / form closing; ignore.
            }
        }

        // Fired on a worker thread by the sink as each error occurs; marshal to the UI thread to append.
        private void OnErrorsAdded(object? sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                BeginInvoke((Action)AppendNewErrors);
            }
            catch (InvalidOperationException)
            {
                // Handle not created yet / form closing; ignore.
            }
        }

        // Fired on the job's worker thread as the phase / file counts change; marshal and update the bar.
        private void OnProgressChanged(object? sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                BeginInvoke((Action)UpdateProgress);
            }
            catch (InvalidOperationException)
            {
                // Handle not created yet / form closing; ignore.
            }
        }

        // Reads the current job phase/counts and drives the action bar's progress. UI thread only.
        private void UpdateProgress()
        {
            var current = job;
            if (current != null)
            {
                actionBar.SetProgress(current.State, current.CompletedFileCount, current.TotalFileCount);
            }
        }

        // Appends any hits collected since we last updated the list. UI thread only.
        private void AppendNewHits()
        {
            var current = sink?.Hits;
            if (current == null || current.Count <= hitsShownCount)
            {
                return;
            }

            hitList.BeginUpdate();
            for (var i = hitsShownCount; i < current.Count; i++)
            {
                var hit = current[i];
                var item = new ListViewItem(hit.File.Path) { Tag = hit };
                item.SubItems.Add(hit.MatchFileOffset.ToString());
                item.SubItems.Add(OneLine(hit.MatchText));
                hitList.Items.Add(item);
            }

            hitsShownCount = current.Count;
            hitList.EndUpdate();
            statusLabel.Text = $"{(lastRunWasReplace ? "Finding replacements" : "Searching")}... {hitsShownCount} hit(s)";
        }

        // Appends any errors collected since we last updated the list, as distinguished rows. UI thread only.
        private void AppendNewErrors()
        {
            var current = sink?.Errors;
            if (current == null || current.Count <= errorsShownCount)
            {
                return;
            }

            hitList.BeginUpdate();
            for (var i = errorsShownCount; i < current.Count; i++)
            {
                var error = current[i];
                // Error rows reuse the columns: path in File, message in Match (Offset left blank). Colored to
                // set them apart from hit rows; the row carries the error so selection can show its detail.
                var item = new ListViewItem(error.Path) { Tag = error, ForeColor = Color.Firebrick };
                item.SubItems.Add(string.Empty);                       // Offset column
                item.SubItems.Add(OneLine(error.Exception.Message));   // Match column
                hitList.Items.Add(item);
            }

            errorsShownCount = current.Count;
            hitList.EndUpdate();
        }

        private void ShowSelectedContext()
        {
            if (hitList.SelectedIndices.Count == 0)
            {
                contextBox.Clear();
                return;
            }

            switch (hitList.Items[hitList.SelectedIndices[0]].Tag)
            {
                case HitRecord hit:
                    // Show context with the match delimited by brackets. In Replace mode, also show what the
                    // match would become (its captured replacement) so the preview is visible before any apply.
                    contextBox.Text = lastRunWasReplace
                        ? hit.PreMatchText + "[" + hit.MatchText + " \u2192 " + hit.ReplacementText + "]" + hit.PostMatchText
                        : hit.PreMatchText + "[" + hit.MatchText + "]" + hit.PostMatchText;
                    break;

                case SearchError error:
                    contextBox.Text = $"{error.Path}: {error.Exception.Message}";
                    break;

                default:
                    contextBox.Clear();
                    break;
            }
        }

        // The single place run/session UI state is decided. Panes disable their inputs/verbs while running;
        // the action bar enables Cancel while running and governs the results verbs otherwise. When a run
        // finishes, the results verbs are available only after a Replace run produced checkable rows (the
        // per-row checkboxes and "any checked" tracking arrive in slice 3, so anyChecked is false for now).
        private void UpdateRunUiState(bool running)
        {
            corePane.SetRunning(running);
            actionBar.SetRunning(running);

            if (running)
            {
                actionBar.SetProgress(SearchJobState.Created, 0, 0);
            }
            else
            {
                actionBar.SetProgress(SearchJobState.Completed, 0, 0);
                actionBar.SetResultsState(hasCheckableResults: lastRunWasReplace && hitsShownCount > 0, anyChecked: false);
            }
        }

        // Swaps the active settings pane into the host panel, sizing the panel to the pane's height.
        private void ShowPane(UserControl pane)
        {
            if (settingsPanel.Controls.Count == 1 && settingsPanel.Controls[0] == pane)
            {
                return;
            }

            settingsPanel.SuspendLayout();
            settingsPanel.Controls.Clear();
            var paneHeight = pane.Height;
            pane.Dock = DockStyle.Fill;
            settingsPanel.Controls.Add(pane);
            settingsPanel.Height = paneHeight;
            settingsPanel.ResumeLayout();

            AcceptButton = pane == corePane ? corePane.SearchButton : null;
        }

        // Collapse newlines/tabs so a multi-line match shows on one list row.
        private static string OneLine(string text) =>
            text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");

        #endregion
    }
}
