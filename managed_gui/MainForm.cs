namespace UnicodeRegEx.Gui
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Data;
    using System.Drawing;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Forms;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Collecting;
    using UnicodeRegEx.Tools.Engine;

    internal partial class MainForm : Form
    {
        // The hits shown in the list, in order (list item N maps to shownHits[N]). Only touched on the UI thread.
        private readonly List<HitRecord> shownHits = new List<HitRecord>();

        private CollectingSink? sink;
        private SearchJob? job;

        public MainForm()
        {
            InitializeComponent();
        }

        #region Event handlers

        private void MainForm_Load(object sender, EventArgs e)
        {
        }

        private async void searchButton_Click(object sender, EventArgs e)
        {
            await StartSearchAsync();
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            job?.Cancel();
        }

        private void hitList_SelectedIndexChanged(object sender, EventArgs e)
        {
            ShowSelectedContext();
        }

        #endregion

        #region Helpers

        private async Task StartSearchAsync()
        {
            if (job != null)
            {
                return; // a search is already running
            }

            var pattern = patternBox.Text;
            if (pattern.Length == 0)
            {
                statusLabel.Text = "Enter a pattern.";
                return;
            }

            // Reset the results.
            hitList.BeginUpdate();
            hitList.Items.Clear();
            hitList.EndUpdate();
            shownHits.Clear();
            contextBox.Clear();

            var request = new SearchRequest { Pattern = pattern, DefaultCodePage = RegExCodePage.Utf8 };
            request.Directories = DirectoryDisposition.RecurseNoLinks;
            request.Paths.Add(pathBox.Text.Length == 0 ? "." : pathBox.Text);

            // Validate up front so an invalid pattern is a friendly message, not a faulted run.
            var problems = request.Validate();
            if (problems.Count > 0)
            {
                statusLabel.Text = "Error: " + request.DescribeProblemForCommandLine(problems[0]);
                return;
            }

            sink = new CollectingSink();
            sink.HitsAdded += OnHitsAdded;
            job = new SearchJob(request, sink);

            SetRunning(true);
            statusLabel.Text = "Searching...";

            try
            {
                await job.RunAsync();
                AppendNewHits();          // final flush of any hits below the last throttled event
                var summary = job.Summary;
                var errorCount = sink.Errors.Count;
                statusLabel.Text = summary.Cancelled
                    ? $"Cancelled. {shownHits.Count} hit(s)."
                    : $"Done. {shownHits.Count} hit(s){(errorCount > 0 ? $", {errorCount} error(s)" : string.Empty)}.";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Error: " + ex.Message;
            }
            finally
            {
                sink.HitsAdded -= OnHitsAdded;
                job.Dispose();
                job = null;
                sink = null;
                SetRunning(false);
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

        // Appends any hits collected since we last updated the list. UI thread only.
        private void AppendNewHits()
        {
            var current = sink?.Hits;
            if (current == null || current.Count <= shownHits.Count)
            {
                return;
            }

            hitList.BeginUpdate();
            for (var i = shownHits.Count; i < current.Count; i++)
            {
                var hit = current[i];
                shownHits.Add(hit);
                var item = new ListViewItem(hit.File.Path);
                item.SubItems.Add(hit.MatchFileOffset.ToString());
                item.SubItems.Add(OneLine(hit.MatchText));
                hitList.Items.Add(item);
            }

            hitList.EndUpdate();
            statusLabel.Text = $"Searching... {shownHits.Count} hit(s)";
        }

        private void ShowSelectedContext()
        {
            if (hitList.SelectedIndices.Count == 0)
            {
                contextBox.Clear();
                return;
            }

            var hit = shownHits[hitList.SelectedIndices[0]];
            // Show context with the match delimited by brackets.
            contextBox.Text = hit.PreMatchText + "[" + hit.MatchText + "]" + hit.PostMatchText;
        }

        private void SetRunning(bool running)
        {
            searchButton.Enabled = !running;
            cancelButton.Enabled = running;
            patternBox.Enabled = !running;
            pathBox.Enabled = !running;
        }

        // Collapse newlines/tabs so a multi-line match shows on one list row.
        private static string OneLine(string text) =>
            text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");

        #endregion
    }
}
