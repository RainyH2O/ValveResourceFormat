namespace GUI.Forms;

partial class FilterForm
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
        checkedListBoxClassNames = new System.Windows.Forms.CheckedListBox();
        buttonFilter = new System.Windows.Forms.Button();
        SuspendLayout();
        //
        // checkedListBoxClassNames
        //
        checkedListBoxClassNames.FormattingEnabled = true;
        checkedListBoxClassNames.Location = new System.Drawing.Point(14, 16);
        checkedListBoxClassNames.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
        checkedListBoxClassNames.Name = "checkedListBoxClassNames";
        checkedListBoxClassNames.Size = new System.Drawing.Size(337, 256);
        checkedListBoxClassNames.TabIndex = 0;
        //
        // buttonFilter
        //
        buttonFilter.Location = new System.Drawing.Point(14, 287);
        buttonFilter.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
        buttonFilter.Name = "buttonFilter";
        buttonFilter.Size = new System.Drawing.Size(337, 30);
        buttonFilter.TabIndex = 1;
        buttonFilter.Text = "Filter";
        buttonFilter.UseVisualStyleBackColor = true;
        buttonFilter.Click += buttonFilter_Click;
        //
        // FilterForm
        //
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(364, 330);
        Controls.Add(buttonFilter);
        Controls.Add(checkedListBoxClassNames);
        KeyPreview = true;
        Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
        Name = "FilterForm";
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        Text = "Filter Window";
        ResumeLayout(false);
    }

    #endregion

    private System.Windows.Forms.CheckedListBox checkedListBoxClassNames;
    private System.Windows.Forms.Button buttonFilter;
}
