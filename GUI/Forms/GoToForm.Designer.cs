namespace GUI.Forms
{
    partial class GoToForm
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
            goButton = new System.Windows.Forms.Button();
            cancelButton = new System.Windows.Forms.Button();
            inputTextBox = new System.Windows.Forms.TextBox();
            typeComboBox = new System.Windows.Forms.ComboBox();
            typeLabel = new System.Windows.Forms.Label();
            inputLabel = new System.Windows.Forms.Label();
            exampleLabel = new System.Windows.Forms.Label();
            SuspendLayout();
            // 
            // goButton
            // 
            goButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            goButton.Location = new System.Drawing.Point(304, 16);
            goButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            goButton.Name = "goButton";
            goButton.Size = new System.Drawing.Size(88, 31);
            goButton.TabIndex = 0;
            goButton.Text = "Go To";
            goButton.UseVisualStyleBackColor = true;
            // 
            // cancelButton
            // 
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            cancelButton.Location = new System.Drawing.Point(304, 53);
            cancelButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new System.Drawing.Size(88, 31);
            cancelButton.TabIndex = 1;
            cancelButton.Text = "Cancel";
            cancelButton.UseVisualStyleBackColor = true;
            // 
            // inputTextBox
            // 
            inputTextBox.Location = new System.Drawing.Point(50, 53);
            inputTextBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            inputTextBox.Name = "inputTextBox";
            inputTextBox.Size = new System.Drawing.Size(246, 25);
            inputTextBox.TabIndex = 2;
            // 
            // typeComboBox
            // 
            typeComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            typeComboBox.FormattingEnabled = true;
            typeComboBox.Location = new System.Drawing.Point(50, 18);
            typeComboBox.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            typeComboBox.Name = "typeComboBox";
            typeComboBox.Size = new System.Drawing.Size(246, 25);
            typeComboBox.TabIndex = 1;
            typeComboBox.SelectedIndexChanged += TypeComboBox_SelectedIndexChanged;
            // 
            // typeLabel
            // 
            typeLabel.AutoSize = true;
            typeLabel.Location = new System.Drawing.Point(14, 23);
            typeLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            typeLabel.Name = "typeLabel";
            typeLabel.Size = new System.Drawing.Size(39, 19);
            typeLabel.TabIndex = 3;
            typeLabel.Text = "Type:";
            // 
            // inputLabel
            // 
            inputLabel.AutoSize = true;
            inputLabel.Location = new System.Drawing.Point(14, 58);
            inputLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            inputLabel.Name = "inputLabel";
            inputLabel.Size = new System.Drawing.Size(42, 19);
            inputLabel.TabIndex = 4;
            inputLabel.Text = "Input:";
            // 
            // exampleLabel
            // 
            exampleLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            exampleLabel.Location = new System.Drawing.Point(50, 86);
            exampleLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            exampleLabel.Name = "exampleLabel";
            exampleLabel.Size = new System.Drawing.Size(246, 19);
            exampleLabel.TabIndex = 5;
            exampleLabel.Text = "e.g. 100 200 300";
            // 
            // GoToForm
            // 
            AcceptButton = goButton;
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            CancelButton = cancelButton;
            ClientSize = new System.Drawing.Size(406, 126);
            Controls.Add(exampleLabel);
            Controls.Add(inputLabel);
            Controls.Add(typeLabel);
            Controls.Add(inputTextBox);
            Controls.Add(typeComboBox);
            Controls.Add(cancelButton);
            Controls.Add(goButton);
            Font = new System.Drawing.Font("Segoe UI", 10F);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "GoToForm";
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            Text = "Go To";
            Load += GoToForm_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Button goButton;
        private System.Windows.Forms.Button cancelButton;
        private System.Windows.Forms.TextBox inputTextBox;
        private System.Windows.Forms.ComboBox typeComboBox;
        private System.Windows.Forms.Label typeLabel;
        private System.Windows.Forms.Label inputLabel;
        private System.Windows.Forms.Label exampleLabel;
    }
} 