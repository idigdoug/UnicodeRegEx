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
            System.Windows.Forms.Label patternLabel;
            System.Windows.Forms.Label pathLabel;
            System.Windows.Forms.ColumnHeader fileColumn;
            System.Windows.Forms.ColumnHeader offsetColumn;
            System.Windows.Forms.ColumnHeader matchColumn;
            this.patternBox = new System.Windows.Forms.TextBox();
            this.pathBox = new System.Windows.Forms.TextBox();
            this.searchButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.statusLabel = new System.Windows.Forms.Label();
            this.hitList = new System.Windows.Forms.ListView();
            this.contextBox = new System.Windows.Forms.TextBox();
            this.split = new System.Windows.Forms.SplitContainer();
            patternLabel = new System.Windows.Forms.Label();
            pathLabel = new System.Windows.Forms.Label();
            fileColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            offsetColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            matchColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            ((System.ComponentModel.ISupportInitialize)(this.split)).BeginInit();
            this.split.Panel1.SuspendLayout();
            this.split.Panel2.SuspendLayout();
            this.split.SuspendLayout();
            this.SuspendLayout();
            // 
            // patternLabel
            // 
            patternLabel.AutoSize = true;
            patternLabel.Location = new System.Drawing.Point(12, 15);
            patternLabel.Name = "patternLabel";
            patternLabel.Size = new System.Drawing.Size(87, 25);
            patternLabel.TabIndex = 0;
            patternLabel.Text = "Pattern:";
            // 
            // pathLabel
            // 
            pathLabel.AutoSize = true;
            pathLabel.Location = new System.Drawing.Point(12, 63);
            pathLabel.Name = "pathLabel";
            pathLabel.Size = new System.Drawing.Size(62, 25);
            pathLabel.TabIndex = 2;
            pathLabel.Text = "Path:";
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
            // patternBox
            // 
            this.patternBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.patternBox.Location = new System.Drawing.Point(105, 12);
            this.patternBox.Name = "patternBox";
            this.patternBox.Size = new System.Drawing.Size(631, 31);
            this.patternBox.TabIndex = 1;
            // 
            // pathBox
            // 
            this.pathBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pathBox.Location = new System.Drawing.Point(105, 60);
            this.pathBox.Name = "pathBox";
            this.pathBox.Size = new System.Drawing.Size(631, 31);
            this.pathBox.TabIndex = 3;
            // 
            // searchButton
            // 
            this.searchButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.searchButton.Location = new System.Drawing.Point(742, 9);
            this.searchButton.Name = "searchButton";
            this.searchButton.Size = new System.Drawing.Size(120, 40);
            this.searchButton.TabIndex = 4;
            this.searchButton.Text = "Search";
            this.searchButton.UseVisualStyleBackColor = true;
            this.searchButton.Click += new System.EventHandler(this.searchButton_Click);
            // 
            // cancelButton
            // 
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelButton.Enabled = false;
            this.cancelButton.Location = new System.Drawing.Point(742, 55);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(120, 40);
            this.cancelButton.TabIndex = 5;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            this.cancelButton.Click += new System.EventHandler(this.cancelButton_Click);
            // 
            // statusLabel
            // 
            this.statusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.statusLabel.Location = new System.Drawing.Point(12, 111);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(850, 25);
            this.statusLabel.TabIndex = 6;
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
            this.hitList.Size = new System.Drawing.Size(859, 287);
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
            this.contextBox.Size = new System.Drawing.Size(859, 117);
            this.contextBox.TabIndex = 0;
            this.contextBox.WordWrap = false;
            // 
            // split
            // 
            this.split.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.split.Location = new System.Drawing.Point(12, 159);
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
            this.split.Size = new System.Drawing.Size(859, 408);
            this.split.SplitterDistance = 287;
            this.split.TabIndex = 9;
            // 
            // MainForm
            // 
            this.AcceptButton = this.searchButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 25F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(874, 579);
            this.Controls.Add(this.split);
            this.Controls.Add(pathLabel);
            this.Controls.Add(patternLabel);
            this.Controls.Add(this.statusLabel);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.searchButton);
            this.Controls.Add(this.pathBox);
            this.Controls.Add(this.patternBox);
            this.MinimumSize = new System.Drawing.Size(600, 400);
            this.Name = "MainForm";
            this.Text = "UnicodeRegEx";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.split.Panel1.ResumeLayout(false);
            this.split.Panel2.ResumeLayout(false);
            this.split.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.split)).EndInit();
            this.split.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox patternBox;
        private System.Windows.Forms.TextBox pathBox;
        private System.Windows.Forms.Button searchButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.ListView hitList;
        private System.Windows.Forms.TextBox contextBox;
        private System.Windows.Forms.SplitContainer split;
    }
}