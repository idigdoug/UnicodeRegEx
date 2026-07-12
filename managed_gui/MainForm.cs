namespace UnicodeRegEx.Gui
{
    using System;
    using System.Drawing;
    using System.Threading.Tasks;
    using System.Windows.Forms;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Collecting;
    using UnicodeRegEx.Tools.Engine;
    using UnicodeRegEx.Tools.Settings;

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

        private CollectingSink? sink;
        private SearchJob? job;

        // The in-progress selective-apply run, if any (distinct from the search job above).
        private ReplaceJob? replaceJob;

        // The verb of the most recent run: true if it was a Replace (results carry replacements and can be
        // applied), false if a plain Find. Consulted by the results UI (selective apply arrives in slice 3).
        private bool lastRunWasReplace;

        // Set while Select All/None bulk-toggles checkboxes, so per-item ItemChecked doesn't recompute the
        // results state on every row (recompute once at the end instead).
        private bool suppressCheckRecompute;

        // Persisted GUI state (MRU lists + preference values), loaded on launch and saved on close.
        private readonly PersistedState state = StateStore.Load(StateStore.DefaultPath);

        // How many entries each MRU list keeps.
        private const int MruCap = 10;

        // Details-pane highlight styling. The match/replacement runs are drawn with a background color (and,
        // for the removed match in Replace mode, strike-through) so the eye can pick them out of the context.
        private static readonly Color MatchHighlight = Color.FromArgb(255, 245, 170);   // pale yellow (Find match)
        private static readonly Color RemovedHighlight = Color.FromArgb(255, 205, 205); // pale red   (replaced-away)
        private static readonly Color AddedHighlight = Color.FromArgb(200, 240, 200);   // pale green (replacement)

        // A strike-through variant of the details box's font, built once from its designer font and reused for
        // the removed-match run. Created after InitializeComponent; disposed with the form.
        private Font? strikeFont;

        // The results context menu ("open with" tools + "Edit this menu..."), rebuilt whenever the tool list
        // changes. Owned by MainForm and assigned to hitList.
        private readonly ContextMenuStrip hitContextMenu = new ContextMenuStrip();

        public MainForm()
        {
            InitializeComponent();

            // Reuse the details box's font, adding strike-through, for the removed-match run in Replace mode.
            strikeFont = new Font(contextBox.Font, FontStyle.Strikeout);

            settings.Paths.Add(Environment.CurrentDirectory);

            // A GUI default: recurse unless a persisted preference (applied below) says otherwise.
            settings.Directories.TrySetValue(DirectoryDisposition.RecurseNoLinks, out _);

            // Restore persisted state into the settings before binding, so the panes reflect it.
            SeedMruDefaults();
            ApplyPreferencesFromState();
            RestoreWorkingValuesFromMru();

            // Both panes edit the same settings object; each fills its controls from it.
            corePane.Bind(settings);
            collapsedPane.Bind(settings);

            corePane.SearchRequested += OnSearchRequested;
            corePane.ReplaceRequested += OnReplaceRequested;
            corePane.CollapseRequested += OnCollapseRequested;
            corePane.AdvancedRequested += OnAdvancedRequested;
            collapsedPane.ExpandRequested += OnExpandRequested;

            // The action bar is a designer control, but its custom intent events are wired here in code: the
            // VS designer does not reliably round-trip += subscriptions for a custom control's events (it
            // drops them on regeneration), so the constructor is the durable home for them.
            actionBar.ApplyRequested += OnApplyRequested;
            actionBar.SelectAllRequested += OnSelectAllRequested;
            actionBar.SelectNoneRequested += OnSelectNoneRequested;
            actionBar.CancelRequested += OnCancelRequested;

            // Result-row check state drives the action bar's results verbs. Error rows are never checkable.
            hitList.ItemCheck += hitList_ItemCheck;
            hitList.ItemChecked += hitList_ItemChecked;
            hitList.KeyDown += hitList_KeyDown;

            // "Open with" tools: seed the list if empty (so the menu always has an entry), build the results
            // context menu, and open the first tool on double-click.
            if (state.GetOpenWithTools().Count == 0)
            {
                state.SetOpenWithTools(OpenWithCommand.DefaultTools());
            }

            hitList.ContextMenuStrip = hitContextMenu;
            hitList.DoubleClick += hitList_DoubleClick;
            RebuildOpenWithMenu();

            // Fill the combo dropdowns from the (seeded) MRU lists, and save on close.
            PopulateMruDropdowns();
            FormClosing += MainForm_FormClosing;

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
            replaceJob?.Cancel();
        }

        // Results verbs — active only when a Replace run has produced checkable rows.
        private async void OnApplyRequested(object? sender, EventArgs e)
        {
            await StartApplyAsync();
        }

        private void OnSelectAllRequested(object? sender, EventArgs e)
        {
            SetAllHitRowsChecked(true);
        }

        private void OnSelectNoneRequested(object? sender, EventArgs e)
        {
            SetAllHitRowsChecked(false);
        }

        // Error rows are not checkable — veto any attempt to check them (Select All / user click / restore).
        private void hitList_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (hitList.Items[e.Index].Tag is SearchError && e.NewValue == CheckState.Checked)
            {
                e.NewValue = CheckState.Unchecked;
            }
        }

        private void hitList_ItemChecked(object? sender, ItemCheckedEventArgs e)
        {
            if (!suppressCheckRecompute)
            {
                RefreshResultsState();
            }
        }

        // Keyboard interactions for managing the results list:
        //   Ctrl+A  — select all rows.
        //   Space   — toggle the checkbox of all selected rows as a unit (check all if any is unchecked,
        //             otherwise uncheck all); suppresses the ListView's default single-item space toggle.
        //   Delete  — uncheck all selected rows.
        // Space/Delete are checkbox operations, so they're natural no-ops on a Find run (no checkboxes).
        private void hitList_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                SelectAllRows();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Space)
            {
                ToggleCheckOfSelectedRows();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                SetCheckOfSelectedRows(false);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void SelectAllRows()
        {
            hitList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in hitList.Items)
                {
                    item.Selected = true;
                }
            }
            finally
            {
                hitList.EndUpdate();
            }
        }

        // Unified toggle: if any selected hit row is unchecked, check them all; otherwise uncheck them all.
        private void ToggleCheckOfSelectedRows()
        {
            var anyUnchecked = false;
            foreach (ListViewItem item in hitList.SelectedItems)
            {
                if (item.Tag is HitRecord && !item.Checked)
                {
                    anyUnchecked = true;
                    break;
                }
            }

            SetCheckOfSelectedRows(anyUnchecked);
        }

        private void SetCheckOfSelectedRows(bool value)
        {
            suppressCheckRecompute = true;
            hitList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in hitList.SelectedItems)
                {
                    // Only hit rows are checkable (the ItemCheck veto blocks error rows anyway).
                    if (item.Tag is HitRecord)
                    {
                        item.Checked = value;
                    }
                }
            }
            finally
            {
                hitList.EndUpdate();
                suppressCheckRecompute = false;
            }

            RefreshResultsState();
        }

        private void OnCollapseRequested(object? sender, EventArgs e)
        {
            collapsedPane.UpdateSummary();
            ShowPane(collapsedPane);

            // Keep the logical focus on the toggle: Hide (core) hands off to Edit (collapsed).
            collapsedPane.FocusExpandButton();
        }

        private void OnAdvancedRequested(object? sender, EventArgs e)
        {
            using var dialog = new AdvancedSettingsForm(settings);
            dialog.ShowDialog(this);

            // Whether committed (OK) or reverted (Cancel restores the snapshot), re-read the primary pane so it
            // reflects the current settings — its checkboxes show indeterminate for a value they can't model.
            corePane.PullFromSettings();
            collapsedPane.UpdateSummary();
        }

        private void OnExpandRequested(object? sender, EventArgs e)
        {
            corePane.PullFromSettings();
            ShowPane(corePane);

            // Symmetric: Edit (collapsed) hands off to Hide (core).
            corePane.FocusCollapseButton();
        }

        private void hitList_SelectedIndexChanged(object sender, EventArgs e)
        {
            ShowSelectedContext();
        }

        #endregion

        #region Helpers

        private async Task StartRunAsync(bool replace)
        {
            if (job != null || replaceJob != null)
            {
                return; // a run is already running
            }

            // The pane already pushed its controls into settings before raising the run request.
            if (settings.Pattern.Length == 0)
            {
                statusLabel.Text = "Enter a pattern.";
                return;
            }

            // Reset the results.
            ClearResults();

            // Validate up front so an invalid pattern is a friendly message, not a faulted run.
            var problems = settings.Validate();
            if (problems.Count > 0)
            {
                statusLabel.Text = "Error: " + problems[0].Message;
                return;
            }

            // The inputs are valid and committed: record them as most-recent (MRU) entries.
            AddCurrentValuesToMru();

            // Find and Replace both run the engine's Match verb (neither edits files here); the only
            // difference is whether each hit records its replacement, so the results can later be applied.
            var request = settings.MakeRequest();
            lastRunWasReplace = replace;

            // Checkboxes are only meaningful for a Replace run (its rows carry replacements to apply).
            hitList.CheckBoxes = replace;

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

        // Applies the checked results: a second run with Verb=Apply whose ApplyingSink rewrites only the chosen
        // matches (re-verifying each against its captured context). Shares the progress / cancel / run-UI
        // scaffolding with StartRunAsync. On completion the results are cleared and a summary is shown.
        // Applies the checked results via a standalone ReplaceJob (no SearchJob / regex re-run): it groups the
        // chosen hits by file, re-verifies each against its captured context, and rewrites files with the
        // previewed replacement bytes. Shares the progress / Cancel / run-UI scaffolding; on completion the
        // results are cleared and a summary is shown.
        private async Task StartApplyAsync()
        {
            if (job != null || replaceJob != null)
            {
                return; // a run is already in progress
            }

            var chosen = CheckedHitRecords();
            if (chosen.Count == 0)
            {
                return; // nothing selected (Apply should be disabled, but guard anyway)
            }

            var apply = new ReplaceJob(chosen, settings.Parallelism.Value);
            replaceJob = apply;
            apply.ProgressChanged += OnProgressChanged;

            UpdateRunUiState(running: true);
            statusLabel.Text = "Applying...";

            try
            {
                await apply.RunAsync();

                var cancelled = apply.State == SearchJobState.Canceled;
                var applied = apply.AppliedCount;
                var skipped = apply.SkippedStaleCount;
                var errorCount = apply.Errors.Count;

                // Selective apply consumes the preview: clear the results and report the outcome.
                ClearResults();
                var parts = $"{applied} replacement(s)";
                if (skipped > 0)
                {
                    parts += $", {skipped} skipped (file changed)";
                }

                if (errorCount > 0)
                {
                    parts += $", {errorCount} error(s)";
                }

                statusLabel.Text = cancelled ? $"Apply cancelled. {parts}." : $"Applied {parts}.";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Error: " + ex.Message;
            }
            finally
            {
                apply.ProgressChanged -= OnProgressChanged;
                apply.Dispose();
                replaceJob = null;
                UpdateRunUiState(running: false);
            }
        }

        // Clears the results list and resets the append cursors / context pane. UI thread only.
        private void ClearResults()
        {
            hitList.BeginUpdate();
            hitList.Items.Clear();
            hitList.EndUpdate();
            hitsShownCount = 0;
            errorsShownCount = 0;
            contextBox.Clear();
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

        // Reads the current run's phase/counts and drives the action bar's progress. UI thread only.
        private void UpdateProgress()
        {
            if (job != null)
            {
                actionBar.SetProgress(job.State, job.CompletedFileCount, job.TotalFileCount);
            }
            else if (replaceJob != null)
            {
                actionBar.SetProgress(replaceJob.State, replaceJob.CompletedFileCount, replaceJob.TotalFileCount);
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
                item.SubItems.Add($"{hit.LineNumber},{hit.ColumnNumber}");   // Position column (Line,Column)
                item.SubItems.Add(OneLine(hit.MatchText));

                // A Replace run's rows are checkable and default to checked (replace-all; deselect a few).
                if (lastRunWasReplace)
                {
                    item.Checked = true;
                }

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
                // Error rows reuse the columns: path in File, message in Match (Position left blank). Colored to
                // set them apart from hit rows; the row carries the error so selection can show its detail.
                var item = new ListViewItem(error.Path) { Tag = error, ForeColor = Color.Firebrick };
                item.SubItems.Add(string.Empty);                       // Position column
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

            contextBox.Clear();

            switch (hitList.Items[hitList.SelectedIndices[0]].Tag)
            {
                case HitRecord hit:
                    // Draw the context with the match (and, in Replace mode, its captured replacement)
                    // highlighted so it stands out. Find: match = highlight. Replace: matched text is struck
                    // through in the "removed" color, immediately followed by the replacement in the "added"
                    // color (so the before/after reads without an arrow).
                    AppendRun(hit.PreMatchText, strike: false, back: null);
                    if (lastRunWasReplace)
                    {
                        AppendRun(hit.MatchText, strike: true, back: RemovedHighlight);
                        AppendRun(hit.ReplacementText, strike: false, back: AddedHighlight);
                    }
                    else
                    {
                        AppendRun(hit.MatchText, strike: false, back: MatchHighlight);
                    }

                    AppendRun(hit.PostMatchText, strike: false, back: null);

                    // Keep the view at the start rather than scrolled to the end after appending.
                    contextBox.SelectionStart = 0;
                    contextBox.SelectionLength = 0;
                    contextBox.ScrollToCaret();
                    break;

                case SearchError error:
                    contextBox.Text = $"{error.Path}: {error.Exception.Message}";
                    break;

                default:
                    contextBox.Clear();
                    break;
            }
        }

        // Appends one styled run to the details box: sets the font (optionally strike-through) and background
        // color for the appended text, restoring the defaults afterward.
        private void AppendRun(string text, bool strike, Color? back)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            contextBox.SelectionStart = contextBox.TextLength;
            contextBox.SelectionLength = 0;
            contextBox.SelectionFont = strike && strikeFont != null ? strikeFont : contextBox.Font;
            contextBox.SelectionBackColor = back ?? contextBox.BackColor;
            contextBox.AppendText(text);
        }

        // The single place run/session UI state is decided. Panes disable their inputs/verbs while running;
        // the action bar enables Cancel while running and governs the results verbs otherwise.
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
                RefreshResultsState();
            }
        }

        // Recomputes the action bar's results-verb enablement from the current rows: Select All/None are
        // available whenever a Replace run produced hit rows; Apply also needs at least one row checked.
        private void RefreshResultsState()
        {
            var hasCheckableResults = lastRunWasReplace && CountHitRows() > 0;
            actionBar.SetResultsState(hasCheckableResults, anyChecked: hasCheckableResults && AnyHitRowChecked());
        }

        private int CountHitRows()
        {
            var count = 0;
            foreach (ListViewItem item in hitList.Items)
            {
                if (item.Tag is HitRecord)
                {
                    count++;
                }
            }

            return count;
        }

        private bool AnyHitRowChecked()
        {
            foreach (ListViewItem item in hitList.Items)
            {
                if (item.Checked && item.Tag is HitRecord)
                {
                    return true;
                }
            }

            return false;
        }

        // Checks or unchecks every hit row (error rows are vetoed by hitList_ItemCheck). Recomputes results
        // state once at the end rather than on every row.
        private void SetAllHitRowsChecked(bool value)
        {
            suppressCheckRecompute = true;
            hitList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in hitList.Items)
                {
                    if (item.Tag is HitRecord)
                    {
                        item.Checked = value;
                    }
                }
            }
            finally
            {
                hitList.EndUpdate();
                suppressCheckRecompute = false;
            }

            RefreshResultsState();
        }

        // The HitRecords of the currently-checked hit rows, in list order.
        private System.Collections.Generic.List<HitRecord> CheckedHitRecords()
        {
            var chosen = new System.Collections.Generic.List<HitRecord>();
            foreach (ListViewItem item in hitList.Items)
            {
                if (item.Checked && item.Tag is HitRecord hit)
                {
                    chosen.Add(hit);
                }
            }

            return chosen;
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

        #region Persistence (MRU + preferences)

        private static readonly (string Key, string[] Seeds)[] MruSeeds =
        {
            (CoreSettingsPane.FileFiltersKey, new[] {
                "*.cs",
                "*.c;*.cpp;*.h;*.hxx",
                "*.txt",
                "*.*",
            }),
        };

        // Any MRU list that has no stored entries yet is filled from its seed list.
        private void SeedMruDefaults()
        {
            foreach (var (key, seeds) in MruSeeds)
            {
                if (seeds.Length > 0 && state.GetMru(key).Count == 0)
                {
                    state.SetMru(key, seeds);
                }
            }
        }

        // Applies every persisted preference (by the setting's LongName) onto the shared settings. List-valued
        // (GlobList) settings are working-state, not preferences, so they are skipped.
        private void ApplyPreferencesFromState()
        {
            foreach (var setting in settings.Settings)
            {
                if (setting.Role != SettingRole.Preference || setting is GlobListSetting)
                {
                    continue;
                }

                var saved = state.GetPreference(setting.LongName);
                if (saved != null)
                {
                    try
                    {
                        setting.Apply(saved, setting.DefaultBinding);
                    }
                    catch (Exception)
                    {
                        // A stale/invalid persisted value must not block startup; keep the current value.
                    }
                }
            }
        }

        // Restores the working-value boxes that should carry over between sessions: In folders (paths) and In
        // files (file globs) take their MRU top. Search/Replace intentionally start empty.
        private void RestoreWorkingValuesFromMru()
        {
            var files = state.GetMru(CoreSettingsPane.FileFiltersKey);
            if (files.Count > 0)
            {
                settings.FileNameFilters.Filters.Clear();
                foreach (var glob in files[0].Split(';'))
                {
                    var trimmed = glob.Trim();
                    if (trimmed.Length != 0)
                    {
                        settings.FileNameFilters.Filters.Add(new GlobFilter(FilterKind.Include, trimmed));
                    }
                }
            }
        }

        // Fills each combo's dropdown list from its MRU.
        private void PopulateMruDropdowns()
        {
            corePane.SetMruItems(CoreSettingsPane.PatternKey, state.GetMru(CoreSettingsPane.PatternKey));
            corePane.SetMruItems(CoreSettingsPane.ReplaceKey, state.GetMru(CoreSettingsPane.ReplaceKey));
            corePane.SetMruItems(CoreSettingsPane.FileFiltersKey, state.GetMru(CoreSettingsPane.FileFiltersKey));
            corePane.SetMruItems(CoreSettingsPane.PathsKey, state.GetMru(CoreSettingsPane.PathsKey));
        }

        // Records the current working-value inputs as most-recent MRU entries (called when a run is launched,
        // after the pane has pushed its controls into settings). Also refreshes the dropdowns so a re-open of
        // the list shows the new top without waiting for a restart.
        private void AddCurrentValuesToMru()
        {
            state.AddMru(CoreSettingsPane.PatternKey, settings.Pattern, MruCap);
            state.AddMru(CoreSettingsPane.ReplaceKey, settings.Replace.Value, MruCap);
            state.AddMru(CoreSettingsPane.FileFiltersKey, settings.FileNameFilters.ToDisplayString(), MruCap);
            state.AddMru(CoreSettingsPane.PathsKey, settings.Paths.Count > 0 ? settings.Paths[0] : ".", MruCap);
            PopulateMruDropdowns();
        }

        // Captures every preference setting's current value into the persisted state (for save on close).
        private void SavePreferencesToState()
        {
            foreach (var setting in settings.Settings)
            {
                if (setting.Role != SettingRole.Preference || setting is GlobListSetting)
                {
                    continue;
                }

                state.SetPreference(setting.LongName, setting.GetPersistedValue());
            }
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            SavePreferencesToState();
            try
            {
                StateStore.Save(StateStore.DefaultPath, state);
            }
            catch (Exception)
            {
                // Failing to persist state on exit should never block closing.
            }
        }

        #endregion

        #region Open-with tools

        // Rebuilds hitList's context menu from the persisted tool list: one item per tool, a separator, then
        // "Edit this menu...". Called on launch and after the editor commits.
        private void RebuildOpenWithMenu()
        {
            hitContextMenu.Items.Clear();

            foreach (var tool in state.GetOpenWithTools())
            {
                var captured = tool;
                var item = new ToolStripMenuItem(tool.Name);
                item.Click += (s, e) => LaunchTool(captured);
                hitContextMenu.Items.Add(item);
            }

            if (hitContextMenu.Items.Count > 0)
            {
                hitContextMenu.Items.Add(new ToolStripSeparator());
            }

            var edit = new ToolStripMenuItem("Edit this menu...");
            edit.Click += (s, e) => EditOpenWithMenu();
            hitContextMenu.Items.Add(edit);
        }

        // Opens the currently selected row with the given tool, substituting its path/line/column. Error rows
        // (no hit) still open their file, falling back to line 1, column 1.
        private void LaunchTool(OpenWithTool tool)
        {
            if (!TryGetSelectedTarget(out var file, out var line, out var column))
            {
                return;
            }

            try
            {
                OpenWithCommand.Launch(tool, file, line, column);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Could not run \"{tool.Name}\":\n{ex.Message}",
                    "Open with",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        // Resolves the selected result row into a file path and 1-based line/column. Returns false if nothing
        // usable is selected.
        private bool TryGetSelectedTarget(out string file, out ulong line, out ulong column)
        {
            file = string.Empty;
            line = 1;
            column = 1;

            if (hitList.SelectedIndices.Count == 0)
            {
                return false;
            }

            switch (hitList.Items[hitList.SelectedIndices[0]].Tag)
            {
                case HitRecord hit:
                    file = hit.File.Path;
                    line = (ulong)hit.LineNumber;
                    column = (ulong)hit.ColumnNumber;
                    return true;

                case SearchError error:
                    // No position for an error row; open the file at the top.
                    file = error.Path;
                    return true;

                default:
                    return false;
            }
        }

        private void hitList_DoubleClick(object? sender, EventArgs e)
        {
            var tools = state.GetOpenWithTools();
            if (tools.Count > 0)
            {
                LaunchTool(tools[0]);
            }
        }

        private void EditOpenWithMenu()
        {
            using var dialog = new OpenWithEditorForm(state.GetOpenWithTools());
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            state.SetOpenWithTools(dialog.Tools);
            RebuildOpenWithMenu();

            // Persist immediately so the edited tools survive even if the app is killed before a clean close.
            try
            {
                StateStore.Save(StateStore.DefaultPath, state);
            }
            catch (Exception)
            {
                // A failed save here is non-fatal; the tools are still active for this session.
            }
        }

        #endregion

        // Collapse newlines/tabs so a multi-line match shows on one list row.
        private static string OneLine(string text) =>
            text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");

        #endregion
    }
}
