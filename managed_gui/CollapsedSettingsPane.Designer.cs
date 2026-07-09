namespace UnicodeRegEx.Gui
{
    partial class CollapsedSettingsPane
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
            this.summaryLabel = new System.Windows.Forms.Label();
            this.expandButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // summaryLabel
            // 
            this.summaryLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.summaryLabel.Location = new System.Drawing.Point(5, 12);
            this.summaryLabel.Margin = new System.Windows.Forms.Padding(5, 0, 5, 0);
            this.summaryLabel.Name = "summaryLabel";
            this.summaryLabel.Size = new System.Drawing.Size(617, 33);
            this.summaryLabel.TabIndex = 0;
            // 
            // expandButton
            // 
            this.expandButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.expandButton.Location = new System.Drawing.Point(698, 5);
            this.expandButton.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.expandButton.Name = "expandButton";
            this.expandButton.Size = new System.Drawing.Size(154, 38);
            this.expandButton.TabIndex = 1;
            this.expandButton.Text = "Edit ▾";
            this.expandButton.Click += new System.EventHandler(this.expandButton_Click);
            // 
            // CollapsedSettingsPane
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(12F, 25F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.summaryLabel);
            this.Controls.Add(this.expandButton);
            this.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.Name = "CollapsedSettingsPane";
            this.Size = new System.Drawing.Size(857, 51);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.Button expandButton;
    }
}
