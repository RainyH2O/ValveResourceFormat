using System.ComponentModel;

namespace GUI.Forms;

partial class EntityListForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();

            if (dataTable != null)
            {
                dataTable.Dispose();
                dataTable = null;
            }
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
        ((System.ComponentModel.ISupportInitialize)entityDataGridView).BeginInit();
        SuspendLayout();
        //
        // entityDataGridView
        //
        entityDataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        entityDataGridView.Dock = System.Windows.Forms.DockStyle.Fill;
        entityDataGridView.Location = new System.Drawing.Point(0, 0);
        entityDataGridView.Name = "entityDataGridView";
        entityDataGridView.Size = new System.Drawing.Size(801, 593);
        entityDataGridView.TabIndex = 0;
        entityDataGridView.Text = "entityDataGridView";
        //
        // EntityListForm
        //
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        ClientSize = new System.Drawing.Size(801, 593);
        Controls.Add(entityDataGridView);
        Text = "EntityListForm";
        ((System.ComponentModel.ISupportInitialize)entityDataGridView).EndInit();
        ResumeLayout(false);
    }

    private System.Windows.Forms.DataGridView entityDataGridView;

    #endregion
}
