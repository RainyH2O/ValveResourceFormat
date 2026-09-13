using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveKeyValue;

namespace GUI.Controls;

internal sealed class SwitchableTextControl : UserControl
{
    private readonly string text;
    private readonly HighlightLanguage language;
    private readonly IReadOnlyList<KvSourceSpan>? sourceMap;
    private readonly ThemedContextMenuStrip contextMenu;
    private Control textControl = null!;
    private bool usingNativeViewer;

    private SwitchableTextControl(string text, HighlightLanguage language, IReadOnlyList<KvSourceSpan>? sourceMap, Control textControl)
    {
        this.text = text;
        this.language = language;
        this.sourceMap = sourceMap;
        Dock = DockStyle.Fill;

        contextMenu = new ThemedContextMenuStrip();
        contextMenu.Items.Add("Use native read-only viewer", null, OnSwitchViewer);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Copy", null, (_, _) => CopySelection());
        contextMenu.Items.Add("Select All", null, (_, _) => SelectAllText());

        SetTextControl(textControl);
    }

    public static Control Create(string text, HighlightLanguage language, IReadOnlyList<KvSourceSpan>? sourceMap = null)
    {
        var textControl = CodeTextBox.Create(text, language, sourceMap);
        return textControl is CodeTextBox
            ? new SwitchableTextControl(text, language, sourceMap, textControl)
            : textControl;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            textControl.ContextMenuStrip = null;
            textControl.Dispose();
            contextMenu.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnSwitchViewer(object? sender, EventArgs e)
    {
        usingNativeViewer = !usingNativeViewer;
        Control replacement = usingNativeViewer
            ? CodeTextBox.CreateBasicTextBox(text)
            : new CodeTextBox(text, language, sourceMap);
        SetTextControl(replacement);
        contextMenu.Items[0].Text = usingNativeViewer
            ? "Use code viewer"
            : "Use native read-only viewer";
        replacement.Focus();
    }

    private void SetTextControl(Control replacement)
    {
        replacement.ContextMenuStrip = contextMenu;
        Controls.Clear();
        if (textControl != null)
        {
            textControl.ContextMenuStrip = null;
            textControl.Dispose();
        }
        textControl = replacement;
        Controls.Add(replacement);
    }

    private void CopySelection()
    {
        switch (textControl)
        {
            case CodeTextBox codeTextBox:
                codeTextBox.Copy();
                break;
            case TextBox textBox:
                textBox.Copy();
                break;
        }
    }

    private void SelectAllText()
    {
        switch (textControl)
        {
            case CodeTextBox codeTextBox:
                codeTextBox.SelectAll();
                break;
            case TextBox textBox:
                textBox.SelectAll();
                break;
        }
    }
}
