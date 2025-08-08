using System.Windows.Forms;

namespace GUI.Forms
{
    partial class EntityInfoControl
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
            var dataGridViewCellStyle1 = new DataGridViewCellStyle();
            var dataGridViewCellStyle2 = new DataGridViewCellStyle();
            var dataGridViewCellStyle3 = new DataGridViewCellStyle();
            tabControl = new ThemedTabControl();
            tabPageProperties = new TabPage();
            dataGridProperties = new DataGridView();
            ColumnName = new DataGridViewTextBoxColumn();
            ColumnValue = new DataGridViewTextBoxColumn();
            tabPageOutputs = new TabPage();
            tabPageInputs = new TabPage();
            dataGridOutputs = new DataGridView();
            Output = new DataGridViewTextBoxColumn();
            TargetEntity = new DataGridViewTextBoxColumn();
            TargetInput = new DataGridViewTextBoxColumn();
            Parameter = new DataGridViewTextBoxColumn();
            Delay = new DataGridViewTextBoxColumn();
            timesToFire = new DataGridViewTextBoxColumn();
            TargetHammerUniqueId = new DataGridViewTextBoxColumn();
            dataGridInputs = new DataGridView();
            SourceHammerUniqueId = new DataGridViewTextBoxColumn();
            SourceName = new DataGridViewTextBoxColumn();
            OutputName = new DataGridViewTextBoxColumn();
            InputName = new DataGridViewTextBoxColumn();
            InputParameter = new DataGridViewTextBoxColumn();
            InputDelay = new DataGridViewTextBoxColumn();
            InputTimesToFire = new DataGridViewTextBoxColumn();
            tabControl.SuspendLayout();
            tabPageProperties.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridProperties).BeginInit();
            tabPageOutputs.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataGridOutputs).BeginInit();
            ((System.ComponentModel.ISupportInitialize)dataGridInputs).BeginInit();
            SuspendLayout();
            //
            // tabControl1
            //
            tabControl.Controls.Add(tabPageProperties);
            tabControl.Controls.Add(tabPageOutputs);
            tabControl.Controls.Add(tabPageInputs);
            tabControl.Size = new System.Drawing.Size(800, 450);
            tabControl.TabIndex = 0;
            //
            // tabPageProperties
            //
            tabPageProperties.Controls.Add(dataGridProperties);
            tabPageProperties.Location = new System.Drawing.Point(4, 24);
            tabPageProperties.Name = "tabPageProperties";
            tabPageProperties.Padding = new Padding(3);
            tabPageProperties.Size = new System.Drawing.Size(792, 422);
            tabPageProperties.TabIndex = 0;
            tabPageProperties.Text = "Properties";
            tabPageProperties.UseVisualStyleBackColor = true;
            //
            // dataGridProperties
            //
            dataGridProperties.AllowUserToAddRows = false;
            dataGridProperties.AllowUserToDeleteRows = false;
            dataGridProperties.AllowUserToResizeRows = false;
            dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.ActiveBorder;
            dataGridProperties.AlternatingRowsDefaultCellStyle = dataGridViewCellStyle1;
            dataGridProperties.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridProperties.BorderStyle = BorderStyle.None;
            dataGridProperties.Columns.AddRange(new DataGridViewColumn[] { ColumnName, ColumnValue });
            dataGridViewCellStyle2.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Cascadia Mono", 10F);
            dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = DataGridViewTriState.True;
            dataGridProperties.DefaultCellStyle = dataGridViewCellStyle2;
            dataGridProperties.Dock = DockStyle.Fill;
            dataGridProperties.Location = new System.Drawing.Point(3, 3);
            dataGridProperties.Name = "dataGridProperties";
            dataGridProperties.ReadOnly = true;
            dataGridProperties.RowHeadersVisible = false;
            dataGridProperties.SelectionMode = DataGridViewSelectionMode.CellSelect;
            dataGridProperties.Size = new System.Drawing.Size(786, 416);
            dataGridProperties.TabIndex = 1;
            //
            // ColumnName
            //
            ColumnName.FillWeight = 30F;
            ColumnName.HeaderText = "Name";
            ColumnName.Name = "ColumnName";
            ColumnName.ReadOnly = true;
            //
            // ColumnValue
            //
            ColumnValue.FillWeight = 70F;
            ColumnValue.HeaderText = "Value";
            ColumnValue.Name = "ColumnValue";
            ColumnValue.ReadOnly = true;
            //
            // tabPageOutputs
            //
            tabPageOutputs.Controls.Add(dataGridOutputs);
            tabPageOutputs.Location = new System.Drawing.Point(4, 24);
            tabPageOutputs.Name = "tabPageOutputs";
            tabPageOutputs.Padding = new Padding(3);
            tabPageOutputs.Size = new System.Drawing.Size(792, 422);
            tabPageOutputs.TabIndex = 1;
            tabPageOutputs.Text = "Outputs";
            tabPageOutputs.UseVisualStyleBackColor = true;
            //
            // tabPageInputs
            //
            tabPageInputs.Controls.Add(dataGridInputs);
            tabPageInputs.Location = new System.Drawing.Point(4, 24);
            tabPageInputs.Name = "tabPageInputs";
            tabPageInputs.Padding = new Padding(3);
            tabPageInputs.Size = new System.Drawing.Size(792, 422);
            tabPageInputs.TabIndex = 2;
            tabPageInputs.Text = "Inputs";
            tabPageInputs.UseVisualStyleBackColor = true;
            //
            // dataGridOutputs
            //
            dataGridOutputs.AllowUserToAddRows = false;
            dataGridOutputs.AllowUserToDeleteRows = false;
            dataGridOutputs.AllowUserToResizeRows = false;
            dataGridOutputs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridOutputs.Columns.AddRange(new DataGridViewColumn[] { Output, TargetEntity, TargetInput, Parameter, Delay, timesToFire, TargetHammerUniqueId });
            dataGridViewCellStyle3.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle3.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle3.Font = new System.Drawing.Font("Cascadia Mono", 10F);
            dataGridViewCellStyle3.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle3.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle3.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle3.WrapMode = DataGridViewTriState.True;
            dataGridOutputs.DefaultCellStyle = dataGridViewCellStyle3;
            dataGridOutputs.Dock = DockStyle.Fill;
            dataGridOutputs.Location = new System.Drawing.Point(3, 3);
            dataGridOutputs.Name = "dataGridOutputs";
            dataGridOutputs.ReadOnly = true;
            dataGridOutputs.RowHeadersVisible = false;
            dataGridOutputs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridOutputs.Size = new System.Drawing.Size(786, 416);
            dataGridOutputs.TabIndex = 0;
            //
            // dataGridInputs
            //
            dataGridInputs.AllowUserToAddRows = false;
            dataGridInputs.AllowUserToDeleteRows = false;
            dataGridInputs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridInputs.Columns.AddRange(new DataGridViewColumn[] { SourceHammerUniqueId, SourceName, OutputName, InputName, InputParameter, InputDelay, InputTimesToFire });
            dataGridViewCellStyle2.Alignment = DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Cascadia Mono", 10F);
            dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle2.WrapMode = DataGridViewTriState.True;
            dataGridInputs.DefaultCellStyle = dataGridViewCellStyle2;
            dataGridInputs.Dock = DockStyle.Fill;
            dataGridInputs.Location = new System.Drawing.Point(3, 3);
            dataGridInputs.Name = "dataGridInputs";
            dataGridInputs.ReadOnly = true;
            dataGridInputs.RowHeadersVisible = false;
            dataGridInputs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridInputs.Size = new System.Drawing.Size(786, 416);
            dataGridInputs.TabIndex = 0;
            //
            // Output
            //
            Output.HeaderText = "Output";
            Output.Name = "Output";
            Output.ReadOnly = true;
            //
            // TargetEntity
            //
            TargetEntity.HeaderText = "Target Entity";
            TargetEntity.Name = "TargetEntity";
            TargetEntity.ReadOnly = true;
            //
            // TargetInput
            //
            TargetInput.HeaderText = "Target Input";
            TargetInput.Name = "TargetInput";
            TargetInput.ReadOnly = true;
            //
            // Parameter
            //
            Parameter.HeaderText = "Parameter";
            Parameter.Name = "Parameter";
            Parameter.ReadOnly = true;
            //
            // Delay
            //
            Delay.HeaderText = "Delay";
            Delay.Name = "Delay";
            Delay.ReadOnly = true;
            //
            // timesToFire
            //
            timesToFire.HeaderText = "Times To Fire";
            timesToFire.Name = "timesToFire";
            timesToFire.ReadOnly = true;
            //
            // TargetHammerUniqueId
            //
            TargetHammerUniqueId.HeaderText = "Target Id";
            TargetHammerUniqueId.Name = "TargetHammerUniqueId";
            TargetHammerUniqueId.ReadOnly = true;
            //
            // SourceHammerUniqueId
            //
            SourceHammerUniqueId.HeaderText = "Source Id";
            SourceHammerUniqueId.Name = "SourceHammerUniqueId";
            SourceHammerUniqueId.ReadOnly = true;
            //
            // SourceName
            //
            SourceName.HeaderText = "Source";
            SourceName.Name = "SourceName";
            SourceName.ReadOnly = true;
            //
            // OutputName
            //
            OutputName.HeaderText = "Output";
            OutputName.Name = "OutputName";
            OutputName.ReadOnly = true;
            //
            // InputName
            //
            InputName.HeaderText = "My Input";
            InputName.Name = "InputName";
            InputName.ReadOnly = true;
            //
            // InputParameter
            //
            InputParameter.HeaderText = "Parameter";
            InputParameter.Name = "Parameter";
            InputParameter.ReadOnly = true;
            //
            // InputDelay
            //
            InputDelay.HeaderText = "Delay";
            InputDelay.Name = "Delay";
            InputDelay.ReadOnly = true;
            //
            // InputTimesToFire
            //
            InputTimesToFire.HeaderText = "Times To Fire";
            InputTimesToFire.Name = "timesToFire";
            InputTimesToFire.ReadOnly = true;
            //
            // EntityInfoControl
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(tabControl);
            Name = "EntityInfoControl";
            Size = new System.Drawing.Size(800, 450);
            tabControl.ResumeLayout(false);
            tabPageProperties.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dataGridProperties).EndInit();
            tabPageOutputs.ResumeLayout(false);
            tabPageInputs.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)dataGridOutputs).EndInit();
            ((System.ComponentModel.ISupportInitialize)dataGridInputs).EndInit();
            ResumeLayout(false);
        }

        #endregion
        private TabPage tabPageProperties;
        private DataGridView dataGridProperties;
        private DataGridViewTextBoxColumn ColumnName;
        private DataGridViewTextBoxColumn ColumnValue;
        private TabPage tabPageOutputs;
        private DataGridView dataGridOutputs;
        private DataGridViewTextBoxColumn Output;
        private DataGridViewTextBoxColumn TargetEntity;
        private DataGridViewTextBoxColumn TargetInput;
        private DataGridViewTextBoxColumn Parameter;
        private DataGridViewTextBoxColumn Delay;
        private DataGridViewTextBoxColumn timesToFire;
        private ThemedTabControl tabControl;
        private DataGridViewTextBoxColumn TargetHammerUniqueId;
        private TabPage tabPageInputs;
        private DataGridView dataGridInputs;
        private DataGridViewTextBoxColumn SourceHammerUniqueId;
        private DataGridViewTextBoxColumn SourceName;
        private DataGridViewTextBoxColumn OutputName;
        private DataGridViewTextBoxColumn InputName;
        private DataGridViewTextBoxColumn InputParameter;
        private DataGridViewTextBoxColumn InputDelay;
        private DataGridViewTextBoxColumn InputTimesToFire;
    }
}
