namespace UnicodeRegEx.Gui
{
    partial class CoreSettingsPane
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
            this.patternLabel = new System.Windows.Forms.Label();
            this.patternBox = new System.Windows.Forms.ComboBox();
            this.replaceLabel = new System.Windows.Forms.Label();
            this.replaceBox = new System.Windows.Forms.ComboBox();
            this.includeFilesLabel = new System.Windows.Forms.Label();
            this.includeFilesBox = new System.Windows.Forms.ComboBox();
            this.pathLabel = new System.Windows.Forms.Label();
            this.pathBox = new System.Windows.Forms.ComboBox();
            this.matchCaseCheck = new System.Windows.Forms.CheckBox();
            this.recurseCheck = new System.Windows.Forms.CheckBox();
            this.perlRegexCheck = new System.Windows.Forms.CheckBox();
            this.searchButton = new System.Windows.Forms.Button();
            this.replaceButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.browseButton = new System.Windows.Forms.Button();
            this.collapseButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // patternLabel
            // 
            this.patternLabel.AutoSize = true;
            this.patternLabel.Location = new System.Drawing.Point(14, 20);
            this.patternLabel.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.patternLabel.Name = "patternLabel";
            this.patternLabel.Size = new System.Drawing.Size(117, 25);
            this.patternLabel.TabIndex = 0;
            this.patternLabel.Text = "Search for:";
            // 
            // patternBox
            // 
            this.patternBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.patternBox.Location = new System.Drawing.Point(163, 13);
            this.patternBox.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.patternBox.Name = "patternBox";
            this.patternBox.Size = new System.Drawing.Size(525, 33);
            this.patternBox.TabIndex = 1;
            // 
            // replaceLabel
            // 
            this.replaceLabel.AutoSize = true;
            this.replaceLabel.Location = new System.Drawing.Point(14, 67);
            this.replaceLabel.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.replaceLabel.Name = "replaceLabel";
            this.replaceLabel.Size = new System.Drawing.Size(141, 25);
            this.replaceLabel.TabIndex = 2;
            this.replaceLabel.Text = "Replace with:";
            // 
            // replaceBox
            // 
            this.replaceBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.replaceBox.Location = new System.Drawing.Point(163, 60);
            this.replaceBox.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.replaceBox.Name = "replaceBox";
            this.replaceBox.Size = new System.Drawing.Size(525, 33);
            this.replaceBox.TabIndex = 3;
            // 
            // includeFilesLabel
            // 
            this.includeFilesLabel.AutoSize = true;
            this.includeFilesLabel.Location = new System.Drawing.Point(14, 113);
            this.includeFilesLabel.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.includeFilesLabel.Name = "includeFilesLabel";
            this.includeFilesLabel.Size = new System.Drawing.Size(80, 25);
            this.includeFilesLabel.TabIndex = 4;
            this.includeFilesLabel.Text = "In files:";
            // 
            // includeFilesBox
            // 
            this.includeFilesBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.includeFilesBox.Location = new System.Drawing.Point(163, 107);
            this.includeFilesBox.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.includeFilesBox.Name = "includeFilesBox";
            this.includeFilesBox.Size = new System.Drawing.Size(525, 33);
            this.includeFilesBox.TabIndex = 5;
            // 
            // pathLabel
            // 
            this.pathLabel.AutoSize = true;
            this.pathLabel.Location = new System.Drawing.Point(14, 160);
            this.pathLabel.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.pathLabel.Name = "pathLabel";
            this.pathLabel.Size = new System.Drawing.Size(106, 25);
            this.pathLabel.TabIndex = 6;
            this.pathLabel.Text = "In folders:";
            // 
            // pathBox
            // 
            this.pathBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pathBox.Location = new System.Drawing.Point(163, 153);
            this.pathBox.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.pathBox.Name = "pathBox";
            this.pathBox.Size = new System.Drawing.Size(525, 33);
            this.pathBox.TabIndex = 7;
            this.pathBox.Text = ".";
            // 
            // matchCaseCheck
            // 
            this.matchCaseCheck.AutoSize = true;
            this.matchCaseCheck.Location = new System.Drawing.Point(163, 196);
            this.matchCaseCheck.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.matchCaseCheck.Name = "matchCaseCheck";
            this.matchCaseCheck.Size = new System.Drawing.Size(142, 29);
            this.matchCaseCheck.TabIndex = 8;
            this.matchCaseCheck.Text = "Match case";
            // 
            // recurseCheck
            // 
            this.recurseCheck.AutoCheck = false;
            this.recurseCheck.AutoSize = true;
            this.recurseCheck.Checked = true;
            this.recurseCheck.CheckState = System.Windows.Forms.CheckState.Checked;
            this.recurseCheck.Location = new System.Drawing.Point(163, 235);
            this.recurseCheck.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.recurseCheck.Name = "recurseCheck";
            this.recurseCheck.Size = new System.Drawing.Size(205, 29);
            this.recurseCheck.TabIndex = 9;
            this.recurseCheck.Text = "Search subfolders";
            this.recurseCheck.ThreeState = true;
            this.recurseCheck.Click += new System.EventHandler(this.recurseCheck_Click);
            // 
            // perlRegexCheck
            // 
            this.perlRegexCheck.AutoCheck = false;
            this.perlRegexCheck.AutoSize = true;
            this.perlRegexCheck.Checked = true;
            this.perlRegexCheck.CheckState = System.Windows.Forms.CheckState.Checked;
            this.perlRegexCheck.Location = new System.Drawing.Point(163, 274);
            this.perlRegexCheck.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.perlRegexCheck.Name = "perlRegexCheck";
            this.perlRegexCheck.Size = new System.Drawing.Size(253, 29);
            this.perlRegexCheck.TabIndex = 10;
            this.perlRegexCheck.Text = "Perl regular expression";
            this.perlRegexCheck.ThreeState = true;
            this.perlRegexCheck.Click += new System.EventHandler(this.perlRegexCheck_Click);
            // 
            // searchButton
            // 
            this.searchButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.searchButton.Location = new System.Drawing.Point(698, 13);
            this.searchButton.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.searchButton.Name = "searchButton";
            this.searchButton.Size = new System.Drawing.Size(154, 38);
            this.searchButton.TabIndex = 11;
            this.searchButton.Text = "Search";
            this.searchButton.Click += new System.EventHandler(this.searchButton_Click);
            // 
            // replaceButton
            // 
            this.replaceButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.replaceButton.Location = new System.Drawing.Point(698, 60);
            this.replaceButton.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.replaceButton.Name = "replaceButton";
            this.replaceButton.Size = new System.Drawing.Size(154, 38);
            this.replaceButton.TabIndex = 12;
            this.replaceButton.Text = "Replace";
            this.replaceButton.Click += new System.EventHandler(this.replaceButton_Click);
            // 
            // cancelButton
            // 
            this.cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelButton.Enabled = false;
            this.cancelButton.Location = new System.Drawing.Point(698, 106);
            this.cancelButton.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(154, 38);
            this.cancelButton.TabIndex = 13;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.Click += new System.EventHandler(this.cancelButton_Click);
            // 
            // browseButton
            // 
            this.browseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.browseButton.Location = new System.Drawing.Point(698, 153);
            this.browseButton.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.browseButton.Name = "browseButton";
            this.browseButton.Size = new System.Drawing.Size(154, 38);
            this.browseButton.TabIndex = 14;
            this.browseButton.Text = "Browse...";
            this.browseButton.Click += new System.EventHandler(this.browseButton_Click);
            // 
            // collapseButton
            // 
            this.collapseButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.collapseButton.Location = new System.Drawing.Point(698, 265);
            this.collapseButton.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.collapseButton.Name = "collapseButton";
            this.collapseButton.Size = new System.Drawing.Size(154, 38);
            this.collapseButton.TabIndex = 15;
            this.collapseButton.Text = "Hide ▴";
            this.collapseButton.Click += new System.EventHandler(this.collapseButton_Click);
            // 
            // CoreSettingsPane
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 25F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.patternLabel);
            this.Controls.Add(this.patternBox);
            this.Controls.Add(this.replaceLabel);
            this.Controls.Add(this.replaceBox);
            this.Controls.Add(this.includeFilesLabel);
            this.Controls.Add(this.includeFilesBox);
            this.Controls.Add(this.pathLabel);
            this.Controls.Add(this.pathBox);
            this.Controls.Add(this.matchCaseCheck);
            this.Controls.Add(this.recurseCheck);
            this.Controls.Add(this.perlRegexCheck);
            this.Controls.Add(this.searchButton);
            this.Controls.Add(this.replaceButton);
            this.Controls.Add(this.cancelButton);
            this.Controls.Add(this.browseButton);
            this.Controls.Add(this.collapseButton);
            this.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.Name = "CoreSettingsPane";
            this.Size = new System.Drawing.Size(857, 310);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label patternLabel;
        private System.Windows.Forms.ComboBox patternBox;
        private System.Windows.Forms.Label replaceLabel;
        private System.Windows.Forms.ComboBox replaceBox;
        private System.Windows.Forms.Label includeFilesLabel;
        private System.Windows.Forms.ComboBox includeFilesBox;
        private System.Windows.Forms.Label pathLabel;
        private System.Windows.Forms.ComboBox pathBox;
        private System.Windows.Forms.CheckBox matchCaseCheck;
        private System.Windows.Forms.CheckBox recurseCheck;
        private System.Windows.Forms.CheckBox perlRegexCheck;
        private System.Windows.Forms.Button searchButton;
        private System.Windows.Forms.Button replaceButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.Button browseButton;
        private System.Windows.Forms.Button collapseButton;
    }
}
