namespace UnicodeRegEx.Gui
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.Windows.Forms.ColumnHeader fileColumn;
            System.Windows.Forms.ColumnHeader offsetColumn;
            System.Windows.Forms.ColumnHeader matchColumn;
            System.Windows.Forms.Label label1;
            this.settingsPanel = new System.Windows.Forms.Panel();
            this.actionBarHost = new System.Windows.Forms.Panel();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.statusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.hitList = new System.Windows.Forms.ListView();
            this.contextBox = new System.Windows.Forms.TextBox();
            this.split = new System.Windows.Forms.SplitContainer();
            fileColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            offsetColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            matchColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            label1 = new System.Windows.Forms.Label();
            this.actionBarHost.SuspendLayout();
            this.statusStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.split)).BeginInit();
            this.split.Panel1.SuspendLayout();
            this.split.Panel2.SuspendLayout();
            this.split.SuspendLayout();
            this.SuspendLayout();
            // 
            // fileColumn
            // 
            fileColumn.Text = "File";
            fileColumn.Width = 380;
            // 
            // offsetColumn
            // 
            offsetColumn.Text = "Offset";
            offsetColumn.Width = 90;
            // 
            // matchColumn
            // 
            matchColumn.Text = "Match";
            matchColumn.Width = 340;
            // 
            // settingsPanel
            // 
            this.settingsPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.settingsPanel.Location = new System.Drawing.Point(0, 0);
            this.settingsPanel.Name = "settingsPanel";
            this.settingsPanel.Size = new System.Drawing.Size(979, 96);
            this.settingsPanel.TabIndex = 0;
            // 
            // actionBarHost
            // 
            this.actionBarHost.Controls.Add(label1);
            this.actionBarHost.Dock = System.Windows.Forms.DockStyle.Top;
            this.actionBarHost.Location = new System.Drawing.Point(0, 96);
            this.actionBarHost.Name = "actionBarHost";
            this.actionBarHost.Size = new System.Drawing.Size(979, 54);
            this.actionBarHost.TabIndex = 1;
            // 
            // statusStrip
            // 
            this.statusStrip.ImageScalingSize = new System.Drawing.Size(32, 32);
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.statusLabel});
            this.statusStrip.Location = new System.Drawing.Point(0, 691);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(979, 42);
            this.statusStrip.TabIndex = 2;
            // 
            // statusLabel
            // 
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(83, 32);
            this.statusLabel.Text = "Ready.";
            // 
            // hitList
            // 
            this.hitList.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            fileColumn,
            offsetColumn,
            matchColumn});
            this.hitList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.hitList.FullRowSelect = true;
            this.hitList.HideSelection = false;
            this.hitList.Location = new System.Drawing.Point(0, 0);
            this.hitList.MultiSelect = false;
            this.hitList.Name = "hitList";
            this.hitList.Size = new System.Drawing.Size(979, 339);
            this.hitList.TabIndex = 0;
            this.hitList.UseCompatibleStateImageBehavior = false;
            this.hitList.View = System.Windows.Forms.View.Details;
            this.hitList.SelectedIndexChanged += new System.EventHandler(this.hitList_SelectedIndexChanged);
            // 
            // contextBox
            // 
            this.contextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contextBox.Font = new System.Drawing.Font("Lucida Console", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.contextBox.Location = new System.Drawing.Point(0, 0);
            this.contextBox.Multiline = true;
            this.contextBox.Name = "contextBox";
            this.contextBox.ReadOnly = true;
            this.contextBox.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.contextBox.Size = new System.Drawing.Size(979, 198);
            this.contextBox.TabIndex = 0;
            this.contextBox.WordWrap = false;
            // 
            // split
            // 
            this.split.Dock = System.Windows.Forms.DockStyle.Fill;
            this.split.Location = new System.Drawing.Point(0, 150);
            this.split.Name = "split";
            this.split.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // split.Panel1
            // 
            this.split.Panel1.Controls.Add(this.hitList);
            // 
            // split.Panel2
            // 
            this.split.Panel2.Controls.Add(this.contextBox);
            this.split.Size = new System.Drawing.Size(979, 541);
            this.split.SplitterDistance = 339;
            this.split.TabIndex = 1;
            // 
            // label1
            // 
            label1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            label1.Dock = System.Windows.Forms.DockStyle.Top;
            label1.Location = new System.Drawing.Point(0, 0);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(979, 1);
            label1.TabIndex = 6;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 25F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(979, 733);
            this.Controls.Add(this.split);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.actionBarHost);
            this.Controls.Add(this.settingsPanel);
            this.MinimumSize = new System.Drawing.Size(600, 400);
            this.Name = "MainForm";
            this.Text = "UnicodeRegEx";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.actionBarHost.ResumeLayout(false);
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.split.Panel1.ResumeLayout(false);
            this.split.Panel2.ResumeLayout(false);
            this.split.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.split)).EndInit();
            this.split.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Panel settingsPanel;
        private System.Windows.Forms.Panel actionBarHost;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel statusLabel;
        private System.Windows.Forms.ListView hitList;
        private System.Windows.Forms.TextBox contextBox;
        private System.Windows.Forms.SplitContainer split;
    }
}