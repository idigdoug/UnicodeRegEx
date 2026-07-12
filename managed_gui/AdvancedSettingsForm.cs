namespace UnicodeRegEx.Gui
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;
    using UnicodeRegEx.Tools;
    using UnicodeRegEx.Tools.Settings;

    /// <summary>
    /// Auto-generated advanced-options dialog. Shows every <see cref="SettingRole.Preference"/> setting on the
    /// shared <see cref="SearchSettings"/>, grouped by category, with a control chosen from each setting's
    /// <see cref="Setting.EditorKind"/>. Edits apply live to the settings; a snapshot taken on open is restored
    /// on Cancel, so OK keeps the edits and Cancel reverts them. List-valued settings are not shown here.
    /// </summary>
    internal partial class AdvancedSettingsForm : Form
    {
        private readonly SearchSettings settings;
        private readonly IReadOnlyDictionary<string, string> snapshot;

        // The generated editor controls, so Reset All can refresh them after resetting the settings.
        private readonly List<Action> refreshers = new List<Action>();

        // Text editors commit on focus loss, but pressing Enter accepts the dialog without a Leave, so each
        // text box also registers a committer that OK runs before closing. Returns false if the value is
        // invalid (the dialog then stays open).
        private readonly List<Func<bool>> committers = new List<Func<bool>>();

        public AdvancedSettingsForm(SearchSettings sharedSettings)
        {
            settings = sharedSettings ?? throw new ArgumentNullException(nameof(sharedSettings));
            snapshot = settings.SnapshotPreferences();

            InitializeComponent();
            BuildRows();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                // Enter accepts the dialog without a text box losing focus, so flush pending text edits here.
                // If any is invalid, keep the dialog open (the committer already showed the error and reverted).
                var allValid = true;
                foreach (var commit in committers)
                {
                    if (!commit())
                    {
                        allValid = false;
                    }
                }

                if (!allValid)
                {
                    e.Cancel = true;
                    return;
                }
            }
            else
            {
                // Cancel (or closing via the X) discards the live edits by restoring the on-open snapshot.
                settings.RestorePreferences(snapshot);
            }

            base.OnFormClosing(e);
        }

        private void BuildRows()
        {
            contentPanel.SuspendLayout();
            contentPanel.Controls.Clear();
            contentPanel.RowCount = 0;
            refreshers.Clear();
            committers.Clear();

            foreach (var group in settings.GroupedSettings)
            {
                var anyInGroup = false;
                foreach (var setting in group.Settings)
                {
                    if (setting.Role != SettingRole.Preference || setting.EditorKind == EditorKind.List)
                    {
                        continue;
                    }

                    if (!anyInGroup)
                    {
                        AddHeader(group.Title);
                        anyInGroup = true;
                    }

                    AddSettingRow(setting);
                }
            }

            // Make sure the last row doesn't get cut off.
            contentPanel.Controls.Add(new Label(), 0, contentPanel.RowCount++);

            contentPanel.ResumeLayout();
        }

        private void AddHeader(string title)
        {
            var label = new Label
            {
                Text = title,
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 4),
            };

            var row = contentPanel.RowCount++;
            contentPanel.Controls.Add(label, 0, row);
            contentPanel.SetColumnSpan(label, 2);
        }

        private void AddSettingRow(Setting setting)
        {
            var label = new Label
            {
                Text = setting.Description,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 6),
            };

            var editor = CreateEditor(setting);
            editor.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            editor.Margin = new Padding(0, 3, 0, 3);

            var row = contentPanel.RowCount++;
            contentPanel.Controls.Add(label, 0, row);
            contentPanel.Controls.Add(editor, 1, row);
        }

        private Control CreateEditor(Setting setting)
        {
            switch (setting.EditorKind)
            {
                case EditorKind.Toggle:
                    return CreateToggle(setting);
                case EditorKind.Choice:
                    return CreateChoice(setting);
                case EditorKind.Integer:
                    return CreateInteger(setting);
                default:
                    return CreateText(setting);
            }
        }

        private Control CreateToggle(Setting setting)
        {
            var check = new CheckBox
            {
                AutoSize = true,
                Checked = setting.GetValue() is bool b && b,
            };

            check.CheckedChanged += (s, e) => ApplyOrRevert(setting, check.Checked, () => check.Checked = setting.GetValue() is bool cur && cur);
            refreshers.Add(() => check.Checked = setting.GetValue() is bool cur && cur);
            return check;
        }

        private Control CreateChoice(Setting setting)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            var choiceSetting = (IChoiceSetting)setting;
            foreach (var choice in choiceSetting.Choices)
            {
                combo.Items.Add(new ChoiceItem(choice));
            }

            SelectCurrentChoice(combo, setting);

            combo.SelectedIndexChanged += (s, e) =>
            {
                if (combo.SelectedItem is ChoiceItem item)
                {
                    ApplyOrRevert(setting, item.Choice.Name, () => SelectCurrentChoice(combo, setting));
                }
            };

            refreshers.Add(() => SelectCurrentChoice(combo, setting));
            return combo;
        }

        private Control CreateInteger(Setting setting)
        {
            var updown = new NumericUpDown
            {
                Minimum = 0,
                Maximum = int.MaxValue,
                Width = 100,
            };

            updown.Value = ToDecimal(setting.GetValue());
            updown.ValueChanged += (s, e) => ApplyOrRevert(setting, (int)updown.Value, () => updown.Value = ToDecimal(setting.GetValue()));
            refreshers.Add(() => updown.Value = ToDecimal(setting.GetValue()));
            return updown;
        }

        private Control CreateText(Setting setting)
        {
            // Use the persisted (round-trippable) form for display: for value settings that parse friendly
            // names (e.g. encoding "utf8", locale "invariant") this is the friendly token, not the raw int.
            var box = new TextBox { Text = setting.GetPersistedValue(), Width = 260 };

            bool Commit() => TryCommitText(setting, box);

            box.Leave += (s, e) => Commit();
            committers.Add(Commit);
            refreshers.Add(() => box.Text = setting.GetPersistedValue());
            return box;
        }

        // Applies a text box's value to its setting. On success returns true; on failure shows the error,
        // reverts the box to the setting's current value, and returns false.
        private bool TryCommitText(Setting setting, TextBox box)
        {
            if (setting.TrySetValue(box.Text, out var error))
            {
                return true;
            }

            MessageBox.Show(this, error, "Invalid value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            box.Text = setting.GetPersistedValue();
            return false;
        }

        // Applies a candidate value via TrySetValue; on failure, shows the error and reverts the control.
        private void ApplyOrRevert(Setting setting, object? value, Action revert)
        {
            if (!setting.TrySetValue(value, out var error))
            {
                MessageBox.Show(this, error, "Invalid value", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                revert();
            }
        }

        private static void SelectCurrentChoice(ComboBox combo, Setting setting)
        {
            // A ChoiceSetting's persisted value is the canonical choice name; match it to the item.
            var current = setting.GetPersistedValue();
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ChoiceItem item && string.Equals(item.Choice.Name, current, StringComparison.Ordinal))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private static decimal ToDecimal(object? value) => value is int i ? i : 0m;

        private void resetAllButton_Click(object? sender, EventArgs e)
        {
            foreach (var setting in settings.Settings)
            {
                if (setting.Role == SettingRole.Preference && setting.EditorKind != EditorKind.List)
                {
                    setting.Reset();
                }
            }

            foreach (var refresh in refreshers)
            {
                refresh();
            }
        }

        // Combo item wrapping a Choice so its canonical name shows and its value is recoverable.
        private sealed class ChoiceItem
        {
            public ChoiceItem(Choice choice)
            {
                Choice = choice;
            }

            public Choice Choice { get; }

            public override string ToString() => Choice.Name;
        }
    }
}
