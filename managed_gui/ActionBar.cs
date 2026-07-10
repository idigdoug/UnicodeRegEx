namespace UnicodeRegEx.Gui
{
    using System;
    using System.Windows.Forms;
    using UnicodeRegEx.Tools.Engine;

    /// <summary>
    /// The always-visible action strip for run/results verbs that are not settings: Apply / Select All /
    /// Select None (results-scoped), a progress bar and Cancel (operation-scoped). Follows the same contract
    /// as the settings panes — it raises intent events and exposes imperative state setters, while
    /// <see cref="MainForm"/> owns all the logic (when each verb is enabled, what it does, and how progress is
    /// driven). Unlike the panes it holds no <see cref="UnicodeRegEx.Tools.SearchSettings"/>; it deals only
    /// with run/results state.
    /// <para>
    /// Layout is fixed: buttons enable/disable but never move or hide, so a click target can't shift out from
    /// under the pointer mid-interaction.
    /// </para>
    /// </summary>
    internal sealed partial class ActionBar : UserControl
    {
        public ActionBar()
        {
            InitializeComponent();
        }

        /// <summary>Raised when the user asks to apply the checked results.</summary>
        public event EventHandler? ApplyRequested;

        /// <summary>Raised when the user asks to check all result rows.</summary>
        public event EventHandler? SelectAllRequested;

        /// <summary>Raised when the user asks to uncheck all result rows.</summary>
        public event EventHandler? SelectNoneRequested;

        /// <summary>Raised when the user asks to cancel the running operation.</summary>
        public event EventHandler? CancelRequested;

        /// <summary>
        /// Reflects run state: Cancel is enabled only while running; the results verbs (Apply / Select All /
        /// Select None) are disabled while running. When not running, results-verb enablement is governed by
        /// <see cref="SetResultsState"/>.
        /// </summary>
        public void SetRunning(bool running)
        {
            cancelButton.Enabled = running;
            if (running)
            {
                applyButton.Enabled = false;
                selectAllButton.Enabled = false;
                selectNoneButton.Enabled = false;
            }
        }

        /// <summary>
        /// Governs the results verbs when not running: Select All/None are available whenever there are
        /// checkable results; Apply additionally requires at least one row checked. (Wired fully in slice 3;
        /// until then callers pass false.)
        /// </summary>
        public void SetResultsState(bool hasCheckableResults, bool anyChecked)
        {
            selectAllButton.Enabled = hasCheckableResults;
            selectNoneButton.Enabled = hasCheckableResults;
            applyButton.Enabled = hasCheckableResults && anyChecked;
        }

        /// <summary>
        /// Drives the progress bar from the job's phase and file counts: marquee while enumerating (total
        /// still growing), determinate while processing, and reset to empty when idle or finished. The bar
        /// stays visible in all states so nothing in the strip shifts.
        /// </summary>
        public void SetProgress(SearchJobState state, int completed, int total)
        {
            switch (state)
            {
                case SearchJobState.Enumerating:
                    progressBar.Style = ProgressBarStyle.Marquee;
                    break;

                case SearchJobState.Processing:
                    progressBar.Style = ProgressBarStyle.Continuous;
                    progressBar.Maximum = total > 0 ? total : 1;
                    progressBar.Value = total > 0 ? System.Math.Min(completed, total) : 0;
                    break;

                default:
                    // Created / Completed / Canceled / Faulted: idle — flat, empty, no marquee.
                    progressBar.Style = ProgressBarStyle.Continuous;
                    progressBar.Value = 0;
                    break;
            }
        }

        private void applyButton_Click(object sender, EventArgs e)
        {
            ApplyRequested?.Invoke(this, EventArgs.Empty);
        }

        private void selectAllButton_Click(object sender, EventArgs e)
        {
            SelectAllRequested?.Invoke(this, EventArgs.Empty);
        }

        private void selectNoneButton_Click(object sender, EventArgs e)
        {
            SelectNoneRequested?.Invoke(this, EventArgs.Empty);
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
