using System.Runtime.InteropServices;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace GUI.Forms
{
    partial class EntityInfoForm : ThemedForm
    {
        public EntityInfoControl EntityInfoControl { get; }
        public ThemedButton? ShowInGraphButton { get; private set; }
        private static WINDOWPLACEMENT? SavedWindowPlacement;

        public EntityInfoForm()
        {
            ClientSize = new System.Drawing.Size(1120, 640);
            MinimumSize = new System.Drawing.Size(640, 360);
            Text = "EntityInfoForm";

            EntityInfoControl = new()
            {
                Dock = DockStyle.Fill
            };
            Controls.Add(EntityInfoControl);

            Icon = Program.MainForm.Icon;
        }

        public void AddShowInGraphButton(EventHandler onClick)
        {
            ShowInGraphButton = new ThemedButton
            {
                Text = "Show in I/O graph",
                Dock = DockStyle.Bottom,
                Height = 32,
            };
            ShowInGraphButton.Click += onClick;
            Controls.Add(ShowInGraphButton);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape && ModifierKeys == Keys.None)
            {
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        protected override void OnShown(System.EventArgs e)
        {
            base.OnShown(e);

            if (SavedWindowPlacement is { } placement)
            {
                PInvoke.SetWindowPlacement((Windows.Win32.Foundation.HWND)Handle, placement);
            }
            else
            {
                var area = Screen.FromControl(this).WorkingArea;
                var size = LogicalToDeviceUnits(new System.Drawing.Size(1120, 640));
                ClientSize = new System.Drawing.Size(Math.Min(size.Width, area.Width * 9 / 10), Math.Min(size.Height, area.Height * 9 / 10));
                Location = new System.Drawing.Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
                    Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var placement = new WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(),
            };

            if (PInvoke.GetWindowPlacement((Windows.Win32.Foundation.HWND)Handle, ref placement))
            {
                SavedWindowPlacement = placement;
            }

            base.OnFormClosing(e);
        }
    }
}
