namespace UnicodeRegEx.Gui
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Forms;
    using UnicodeRegEx;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Collecting;
    using UnicodeRegEx.Tools.Engine;

    /// <summary>
    /// Slice 1b: a minimal find tool. Enter a pattern and a path, run a search, browse the hits, and see
    /// each hit's surrounding context. No options page and no replace yet (later slices).
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly TextBox patternBox;
        private readonly TextBox pathBox;
        private readonly Button searchButton;
        private readonly Button cancelButton;
        private readonly Label statusLabel;
        private readonly ListView hitList;
        private readonly TextBox contextBox;

        // The hits shown in the list, in order (list item N maps to shownHits[N]). Only touched on the UI thread.
        private readonly List<HitRecord> shownHits = new List<HitRecord>();

        private CollectingSink? sink;
        private SearchJob? job;

        public MainForm()
        {
            Text = "UnicodeRegEx";
            Width = 900;
            Height = 650;
            MinimumSize = new Size(600, 400);

            // --- Inputs row ---
            var patternLabel = new Label { Text = "Pattern:", AutoSize = true, Left = 8, Top = 12 };
            patternBox = new TextBox { Left = 70, Top = 8, Width = 500, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            var pathLabel = new Label { Text = "Path:", AutoSize = true, Left = 8, Top = 40 };
            pathBox = new TextBox { Left = 70, Top = 36, Width = 500, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Text = "." };

            searchButton = new Button { Text = "Search", Left = 580, Top = 7, Width = 90, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            cancelButton = new Button { Text = "Cancel", Left = 580, Top = 35, Width = 90, Anchor = AnchorStyles.Top | AnchorStyles.Right, Enabled = false };
            searchButton.Click += async (_, _) => await StartSearchAsync();
            cancelButton.Click += (_, _) => job?.Cancel();
            AcceptButton = searchButton;

            statusLabel = new Label { Left = 8, Top = 68, Width = 660, AutoSize = false, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, Text = "Ready." };

            // --- Results split ---
            var split = new SplitContainer
            {
                Left = 8,
                Top = 92,
                Width = 860,
                Height = 500,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 320,
            };

            hitList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
            };
            hitList.Columns.Add("File", 380);
            hitList.Columns.Add("Offset", 90);
            hitList.Columns.Add("Match", 340);
            hitList.SelectedIndexChanged += (_, _) => ShowSelectedContext();

            contextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9f),
            };

            split.Panel1.Controls.Add(hitList);
            split.Panel2.Controls.Add(contextBox);

            Controls.Add(patternLabel);
            Controls.Add(patternBox);
            Controls.Add(pathLabel);
            Controls.Add(pathBox);
            Controls.Add(searchButton);
            Controls.Add(cancelButton);
            Controls.Add(statusLabel);
            Controls.Add(split);
        }

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
    }
}
