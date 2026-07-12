namespace UnicodeRegEx.Gui
{
    partial class OpenWithEditorForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.toolsList = new System.Windows.Forms.ListBox();
            this.moveUpButton = new System.Windows.Forms.Button();
            this.moveDownButton = new System.Windows.Forms.Button();
            this.nameLabel = new System.Windows.Forms.Label();
            this.nameBox = new System.Windows.Forms.TextBox();
            this.commandLabel = new System.Windows.Forms.Label();
            this.commandBox = new System.Windows.Forms.TextBox();
            this.hintLabel = new System.Windows.Forms.Label();
            this.addButton = new System.Windows.Forms.Button();
            this.updateButton = new System.Windows.Forms.Button();
            this.removeButton = new System.Windows.Forms.Button();
            this.okButton = new System.Windows.Forms.Button();
            this.cancelButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // toolsList
            // 
            this.toolsList.FormattingEnabled = true;
            this.toolsList.IntegralHeight = false;
            this.toolsList.Location = new System.Drawing.Point(12, 12);
            this.toolsList.Name = "toolsList";
            this.toolsList.Size = new System.Drawing.Size(360, 180);
            this.toolsList.TabIndex = 0;
            this.toolsList.SelectedIndexChanged += new System.EventHandler(this.toolsList_SelectedIndexChanged);
            // 
            // moveUpButton
            // 
            this.moveUpButton.Location = new System.Drawing.Point(378, 12);
            this.moveUpButton.Name = "moveUpButton";
            this.moveUpButton.Size = new System.Drawing.Size(90, 27);
            this.moveUpButton.TabIndex = 1;
            this.moveUpButton.Text = "Move &Up";
            this.moveUpButton.UseVisualStyleBackColor = true;
            this.moveUpButton.Click += new System.EventHandler(this.moveUpButton_Click);
            // 
            // moveDownButton
            // 
            this.moveDownButton.Location = new System.Drawing.Point(378, 45);
            this.moveDownButton.Name = "moveDownButton";
            this.moveDownButton.Size = new System.Drawing.Size(90, 27);
            this.moveDownButton.TabIndex = 2;
            this.moveDownButton.Text = "Move &Down";
            this.moveDownButton.UseVisualStyleBackColor = true;
            this.moveDownButton.Click += new System.EventHandler(this.moveDownButton_Click);
            // 
            // removeButton
            // 
            this.removeButton.Location = new System.Drawing.Point(378, 165);
            this.removeButton.Name = "removeButton";
            this.removeButton.Size = new System.Drawing.Size(90, 27);
            this.removeButton.TabIndex = 3;
            this.removeButton.Text = "&Remove";
            this.removeButton.UseVisualStyleBackColor = true;
            this.removeButton.Click += new System.EventHandler(this.removeButton_Click);
            // 
            // nameLabel
            // 
            this.nameLabel.AutoSize = true;
            this.nameLabel.Location = new System.Drawing.Point(12, 205);
            this.nameLabel.Name = "nameLabel";
            this.nameLabel.Size = new System.Drawing.Size(42, 15);
            this.nameLabel.TabIndex = 4;
            this.nameLabel.Text = "&Name:";
            // 
            // nameBox
            // 
            this.nameBox.Location = new System.Drawing.Point(110, 202);
            this.nameBox.Name = "nameBox";
            this.nameBox.Size = new System.Drawing.Size(358, 23);
            this.nameBox.TabIndex = 5;
            // 
            // commandLabel
            // 
            this.commandLabel.AutoSize = true;
            this.commandLabel.Location = new System.Drawing.Point(12, 234);
            this.commandLabel.Name = "commandLabel";
            this.commandLabel.Size = new System.Drawing.Size(92, 15);
            this.commandLabel.TabIndex = 6;
            this.commandLabel.Text = "&Command line:";
            // 
            // commandBox
            // 
            this.commandBox.Location = new System.Drawing.Point(110, 231);
            this.commandBox.Name = "commandBox";
            this.commandBox.Size = new System.Drawing.Size(358, 23);
            this.commandBox.TabIndex = 7;
            // 
            // hintLabel
            // 
            this.hintLabel.AutoSize = true;
            this.hintLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.hintLabel.Location = new System.Drawing.Point(110, 258);
            this.hintLabel.Name = "hintLabel";
            this.hintLabel.Size = new System.Drawing.Size(320, 15);
            this.hintLabel.TabIndex = 8;
            this.hintLabel.Text = "Tokens: $F = file, $L = line, $C = column, $$ = literal $";
            // 
            // addButton
            // 
            this.addButton.Location = new System.Drawing.Point(110, 285);
            this.addButton.Name = "addButton";
            this.addButton.Size = new System.Drawing.Size(90, 27);
            this.addButton.TabIndex = 9;
            this.addButton.Text = "&Add";
            this.addButton.UseVisualStyleBackColor = true;
            this.addButton.Click += new System.EventHandler(this.addButton_Click);
            // 
            // updateButton
            // 
            this.updateButton.Location = new System.Drawing.Point(206, 285);
            this.updateButton.Name = "updateButton";
            this.updateButton.Size = new System.Drawing.Size(90, 27);
            this.updateButton.TabIndex = 10;
            this.updateButton.Text = "&Update";
            this.updateButton.UseVisualStyleBackColor = true;
            this.updateButton.Click += new System.EventHandler(this.updateButton_Click);
            // 
            // okButton
            // 
            this.okButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.okButton.Location = new System.Drawing.Point(288, 330);
            this.okButton.Name = "okButton";
            this.okButton.Size = new System.Drawing.Size(90, 27);
            this.okButton.TabIndex = 11;
            this.okButton.Text = "OK";
            this.okButton.UseVisualStyleBackColor = true;
            this.okButton.Click += new System.EventHandler(this.okButton_Click);
            // 
            // cancelButton
            // 
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(384, 330);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(90, 27);
            this.cancelButton.TabIndex = 12;
            this.cancelButton.Text = "Cancel";
            this.cancelButton.UseVisualStyleBackColor = true;
            // 
            // OpenWithEditorForm
            // 
            this.AcceptButton = this.okButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(486, 369);
            this.Controls.Add(this.toolsList);
            this.Controls.Add(this.moveUpButton);
            this.Controls.Add(this.moveDownButton);
            this.Controls.Add(this.removeButton);
            this.Controls.Add(this.nameLabel);
            this.Controls.Add(this.nameBox);
            this.Controls.Add(this.commandLabel);
            this.Controls.Add(this.commandBox);
            this.Controls.Add(this.hintLabel);
            this.Controls.Add(this.addButton);
            this.Controls.Add(this.updateButton);
            this.Controls.Add(this.okButton);
            this.Controls.Add(this.cancelButton);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OpenWithEditorForm";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Edit Open-With Tools";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.ListBox toolsList;
        private System.Windows.Forms.Button moveUpButton;
        private System.Windows.Forms.Button moveDownButton;
        private System.Windows.Forms.Button removeButton;
        private System.Windows.Forms.Label nameLabel;
        private System.Windows.Forms.TextBox nameBox;
        private System.Windows.Forms.Label commandLabel;
        private System.Windows.Forms.TextBox commandBox;
        private System.Windows.Forms.Label hintLabel;
        private System.Windows.Forms.Button addButton;
        private System.Windows.Forms.Button updateButton;
        private System.Windows.Forms.Button okButton;
        private System.Windows.Forms.Button cancelButton;
    }
}
