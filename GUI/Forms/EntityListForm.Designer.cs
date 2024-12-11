namespace GUI.Forms;

partial class EntityListForm
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

            if (_dataTable != null)
            {
                _dataTable.Dispose();
                _dataTable = null;
            }
            entityDataGridView.CellClick -= EntityDataGridView_CellClick;
            entityDataGridView.CellDoubleClick -= EntityDataGridView_CellDoubleClick;
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
        entityDataGridView = new System.Windows.Forms.DataGridView();
        tableLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
        filtersPanel = new System.Windows.Forms.TableLayoutPanel();
        filterLabel = new System.Windows.Forms.Label();
        keyValueFilter = new System.Windows.Forms.TableLayoutPanel();
        keyTextBox = new System.Windows.Forms.TextBox();
        equalLabel = new System.Windows.Forms.Label();
        valueTextBox = new System.Windows.Forms.TextBox();
        exactMatchCheckBox = new System.Windows.Forms.CheckBox();
        entityIOFilter = new System.Windows.Forms.TableLayoutPanel();
        outputTextBox = new System.Windows.Forms.TextBox();
        targetTextBox = new System.Windows.Forms.TextBox();
        inputTextBox = new System.Windows.Forms.TextBox();
        exportButton = new System.Windows.Forms.Button();
        ((System.ComponentModel.ISupportInitialize)entityDataGridView).BeginInit();
        tableLayoutPanel.SuspendLayout();
        filtersPanel.SuspendLayout();
        keyValueFilter.SuspendLayout();
        entityIOFilter.SuspendLayout();
        SuspendLayout();
        //
        // entityDataGridView
        //
        entityDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        entityDataGridView.Dock = System.Windows.Forms.DockStyle.Fill;
        entityDataGridView.Location = new System.Drawing.Point(3, 3);
        entityDataGridView.Name = "entityDataGridView";
        entityDataGridView.ReadOnly = true;
        entityDataGridView.Size = new System.Drawing.Size(678, 460);
        entityDataGridView.TabIndex = 0;
        entityDataGridView.Text = "entityDataGridView";
        entityDataGridView.CellClick += EntityDataGridView_CellClick;
        entityDataGridView.CellDoubleClick += EntityDataGridView_CellDoubleClick;
        //
        // tableLayoutPanel
        //
        tableLayoutPanel.ColumnCount = 1;
        tableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        tableLayoutPanel.Controls.Add(entityDataGridView, 0, 0);
        tableLayoutPanel.Controls.Add(filtersPanel, 0, 1);
        tableLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
        tableLayoutPanel.Location = new System.Drawing.Point(0, 0);
        tableLayoutPanel.Name = "tableLayoutPanel";
        tableLayoutPanel.RowCount = 2;
        tableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
        tableLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
        tableLayoutPanel.Size = new System.Drawing.Size(684, 561);
        tableLayoutPanel.TabIndex = 1;
        //
        // filtersPanel
        //
        filtersPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        filtersPanel.ColumnCount = 1;
        filtersPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
        filtersPanel.Controls.Add(filterLabel, 0, 0);
        filtersPanel.Controls.Add(keyValueFilter, 0, 1);
        filtersPanel.Controls.Add(entityIOFilter, 0, 2);
        filtersPanel.Location = new System.Drawing.Point(3, 469);
        filtersPanel.Name = "filtersPanel";
        filtersPanel.RowCount = 3;
        filtersPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20F));
        filtersPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 40F));
        filtersPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 40F));
        filtersPanel.Size = new System.Drawing.Size(320, 89);
        filtersPanel.TabIndex = 0;
        //
        // filterLabel
        //
        filterLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        filterLabel.Location = new System.Drawing.Point(3, 0);
        filterLabel.Name = "filterLabel";
        filterLabel.Size = new System.Drawing.Size(312, 17);
        filterLabel.TabIndex = 0;
        filterLabel.Text = "Filters";
        filterLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        //
        // keyValueFilter
        //
        keyValueFilter.ColumnCount = 4;
        keyValueFilter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 35F));
        keyValueFilter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 10F));
        keyValueFilter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 35F));
        keyValueFilter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 20F));
        keyValueFilter.Controls.Add(keyTextBox, 0, 0);
        keyValueFilter.Controls.Add(equalLabel, 1, 0);
        keyValueFilter.Controls.Add(valueTextBox, 2, 0);
        keyValueFilter.Controls.Add(exactMatchCheckBox, 3, 0);
        keyValueFilter.Dock = System.Windows.Forms.DockStyle.Fill;
        keyValueFilter.Location = new System.Drawing.Point(3, 20);
        keyValueFilter.Name = "keyValueFilter";
        keyValueFilter.RowCount = 1;
        keyValueFilter.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
        keyValueFilter.Size = new System.Drawing.Size(312, 28);
        keyValueFilter.TabIndex = 1;
        keyValueFilter.Tag = "keyValueFilter";
        //
        // keyTextBox
        //
        keyTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
        keyTextBox.Location = new System.Drawing.Point(3, 3);
        keyTextBox.Name = "keyTextBox";
        keyTextBox.PlaceholderText = "Key";
        keyTextBox.Size = new System.Drawing.Size(103, 23);
        keyTextBox.TabIndex = 1;
        keyTextBox.Tag = "Key";
        keyTextBox.TextChanged += FilterPanelTextBox_TextChanged;
        //
        // equalLabel
        //
        equalLabel.Dock = System.Windows.Forms.DockStyle.Fill;
        equalLabel.Location = new System.Drawing.Point(112, 0);
        equalLabel.Name = "equalLabel";
        equalLabel.Size = new System.Drawing.Size(25, 28);
        equalLabel.TabIndex = 2;
        equalLabel.Text = "=";
        equalLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        //
        // valueTextBox
        //
        valueTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
        valueTextBox.Location = new System.Drawing.Point(143, 3);
        valueTextBox.Name = "valueTextBox";
        valueTextBox.PlaceholderText = "Value";
        valueTextBox.Size = new System.Drawing.Size(103, 23);
        valueTextBox.TabIndex = 3;
        valueTextBox.Tag = "Value";
        valueTextBox.TextChanged += FilterPanelTextBox_TextChanged;
        //
        // exactMatchCheckBox
        //
        exactMatchCheckBox.Dock = System.Windows.Forms.DockStyle.Fill;
        exactMatchCheckBox.Location = new System.Drawing.Point(252, 3);
        exactMatchCheckBox.Name = "exactMatchCheckBox";
        exactMatchCheckBox.Size = new System.Drawing.Size(57, 22);
        exactMatchCheckBox.TabIndex = 4;
        exactMatchCheckBox.Text = "Exact";
        exactMatchCheckBox.CheckedChanged += FilterPanelTextBox_TextChanged;
        //
        // entityIOFilter
        //
        entityIOFilter.ColumnCount = 3;
        entityIOFilter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.3333321F));
        entityIOFilter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.3333359F));
        entityIOFilter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.3333359F));
        entityIOFilter.Controls.Add(outputTextBox, 0, 0);
        entityIOFilter.Controls.Add(targetTextBox, 1, 0);
        entityIOFilter.Controls.Add(inputTextBox, 2, 0);
        entityIOFilter.Dock = System.Windows.Forms.DockStyle.Fill;
        entityIOFilter.Location = new System.Drawing.Point(3, 54);
        entityIOFilter.Name = "entityIOFilter";
        entityIOFilter.RowCount = 2;
        entityIOFilter.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
        entityIOFilter.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 20F));
        entityIOFilter.Size = new System.Drawing.Size(312, 30);
        entityIOFilter.TabIndex = 1;
        entityIOFilter.Tag = "entityIOFilter";
        //
        // outputTextBox
        //
        outputTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
        outputTextBox.Location = new System.Drawing.Point(3, 3);
        outputTextBox.Name = "outputTextBox";
        outputTextBox.PlaceholderText = "Output";
        outputTextBox.Size = new System.Drawing.Size(97, 23);
        outputTextBox.TabIndex = 0;
        outputTextBox.Tag = "Output";
        outputTextBox.TextChanged += FilterPanelTextBox_TextChanged;
        //
        // targetTextBox
        //
        targetTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
        targetTextBox.Location = new System.Drawing.Point(106, 3);
        targetTextBox.Name = "targetTextBox";
        targetTextBox.PlaceholderText = "Target";
        targetTextBox.Size = new System.Drawing.Size(98, 23);
        targetTextBox.TabIndex = 1;
        targetTextBox.Tag = "Target";
        targetTextBox.TextChanged += FilterPanelTextBox_TextChanged;
        //
        // inputTextBox
        //
        inputTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
        inputTextBox.Location = new System.Drawing.Point(210, 3);
        inputTextBox.Name = "inputTextBox";
        inputTextBox.PlaceholderText = "Input";
        inputTextBox.Size = new System.Drawing.Size(99, 23);
        inputTextBox.TabIndex = 2;
        inputTextBox.Tag = "Input";
        inputTextBox.TextChanged += FilterPanelTextBox_TextChanged;
        //
        // exportButton
        //
        exportButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
        exportButton.Location = new System.Drawing.Point(590, 525);
        exportButton.Name = "exportButton";
        exportButton.Size = new System.Drawing.Size(80, 25);
        exportButton.TabIndex = 2;
        exportButton.Text = "Export";
        exportButton.UseVisualStyleBackColor = true;
        exportButton.Click += ExportButton_Click;
        //
        // EntityListForm
        //
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(684, 561);
        Controls.Add(exportButton);
        Controls.Add(tableLayoutPanel);
        Name = "EntityListForm";
        Text = "EntityListForm";
        ((System.ComponentModel.ISupportInitialize)entityDataGridView).EndInit();
        tableLayoutPanel.ResumeLayout(false);
        filtersPanel.ResumeLayout(false);
        keyValueFilter.ResumeLayout(false);
        keyValueFilter.PerformLayout();
        entityIOFilter.ResumeLayout(false);
        entityIOFilter.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private System.Windows.Forms.DataGridView entityDataGridView;
    private System.Windows.Forms.TableLayoutPanel tableLayoutPanel;
    private System.Windows.Forms.Label filterLabel;
    private System.Windows.Forms.TableLayoutPanel filtersPanel;
    private System.Windows.Forms.TextBox keyTextBox;
    private System.Windows.Forms.Label equalLabel;
    private System.Windows.Forms.TextBox valueTextBox;
    private System.Windows.Forms.CheckBox exactMatchCheckBox;
    private System.Windows.Forms.TableLayoutPanel keyValueFilter;
    private System.Windows.Forms.TextBox outputTextBox;
    private System.Windows.Forms.TextBox targetTextBox;
    private System.Windows.Forms.TextBox inputTextBox;
    private System.Windows.Forms.TableLayoutPanel entityIOFilter;
    private System.Windows.Forms.Button exportButton;
}
