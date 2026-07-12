namespace UnicodeRegEx.Gui
{
    using System;
    using System.Collections.Generic;
    using System.Windows.Forms;
    using UnicodeRegEx.Tools;

    /// <summary>
    /// Modal editor for the results context menu's "open with" tools. Works on a staging copy of the list;
    /// <see cref="Tools"/> reflects the edited list and is meaningful only when the dialog returns
    /// <see cref="DialogResult.OK"/>.
    /// </summary>
    internal partial class OpenWithEditorForm : Form
    {
        private readonly List<OpenWithTool> tools = new List<OpenWithTool>();

        public OpenWithEditorForm(IEnumerable<OpenWithTool> initialTools)
        {
            InitializeComponent();

            foreach (var tool in initialTools)
            {
                tools.Add(new OpenWithTool(tool.Name, tool.CommandLine));
            }

            RefreshList(tools.Count > 0 ? 0 : -1);
        }

        /// <summary>The edited tool list (valid after the dialog returns OK).</summary>
        public IReadOnlyList<OpenWithTool> Tools => tools;

        private int SelectedIndex => toolsList.SelectedIndex;

        private void RefreshList(int selectIndex)
        {
            toolsList.BeginUpdate();
            toolsList.Items.Clear();
            foreach (var tool in tools)
            {
                toolsList.Items.Add(Describe(tool));
            }

            toolsList.EndUpdate();

            if (selectIndex >= 0 && selectIndex < tools.Count)
            {
                toolsList.SelectedIndex = selectIndex;
            }

            UpdateButtonState();
        }

        private static string Describe(OpenWithTool tool) =>
            string.IsNullOrEmpty(tool.CommandLine) ? tool.Name : $"{tool.Name}  ({tool.CommandLine})";

        private void UpdateButtonState()
        {
            var index = SelectedIndex;
            var has = index >= 0;
            updateButton.Enabled = has;
            removeButton.Enabled = has;
            moveUpButton.Enabled = index > 0;
            moveDownButton.Enabled = has && index < tools.Count - 1;
        }

        private void toolsList_SelectedIndexChanged(object? sender, EventArgs e)
        {
            var index = SelectedIndex;
            if (index >= 0)
            {
                nameBox.Text = tools[index].Name;
                commandBox.Text = tools[index].CommandLine;
            }

            UpdateButtonState();
        }

        private void addButton_Click(object? sender, EventArgs e)
        {
            var name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                nameBox.Focus();
                return;
            }

            tools.Add(new OpenWithTool(name, commandBox.Text));
            RefreshList(tools.Count - 1);
        }

        private void updateButton_Click(object? sender, EventArgs e)
        {
            var index = SelectedIndex;
            if (index < 0)
            {
                return;
            }

            var name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                nameBox.Focus();
                return;
            }

            tools[index].Name = name;
            tools[index].CommandLine = commandBox.Text;
            RefreshList(index);
        }

        private void removeButton_Click(object? sender, EventArgs e)
        {
            var index = SelectedIndex;
            if (index < 0)
            {
                return;
            }

            tools.RemoveAt(index);
            RefreshList(index < tools.Count ? index : tools.Count - 1);
        }

        private void moveUpButton_Click(object? sender, EventArgs e) => MoveSelected(-1);

        private void moveDownButton_Click(object? sender, EventArgs e) => MoveSelected(1);

        private void MoveSelected(int delta)
        {
            var index = SelectedIndex;
            var target = index + delta;
            if (index < 0 || target < 0 || target >= tools.Count)
            {
                return;
            }

            var tool = tools[index];
            tools.RemoveAt(index);
            tools.Insert(target, tool);
            RefreshList(target);
        }

        private void okButton_Click(object? sender, EventArgs e)
        {
            // DialogResult.OK is set by the button; nothing else to commit (Tools is the staging list).
        }
    }
}
