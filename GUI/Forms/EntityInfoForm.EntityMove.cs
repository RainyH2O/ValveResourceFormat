using System.Windows.Forms;
using GUI.Controls;

namespace GUI.Forms;

partial class EntityInfoForm
{
    public ThemedButton? MoveEntityButton { get; private set; }

    public void AddMoveEntityButton(EventHandler onClick)
    {
        MoveEntityButton = new ThemedButton
        {
            Text = "Move entity...",
            Dock = DockStyle.Bottom,
            Height = 32,
        };
        MoveEntityButton.Click += onClick;
        Controls.Add(MoveEntityButton);
    }
}
