using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuickTrigger
{
    // Listen only while this small binding dialog has focus; no input is recorded elsewhere.
    internal sealed class BindingForm : Form
    {
        private readonly Timer timer = new Timer();
        private readonly Label hint;
        private readonly bool sendClicks;
        private bool ready;
        public int SelectedKey { get; private set; }

        // Wait for the opening click to be released so it cannot become the binding.
        public BindingForm(bool sendClicks)
        {
            this.sendClicks = sendClicks;
            Text = "Choose hold button";
            Font = new Font("Segoe UI", 10);
            BackColor = Ui.Background;
            ClientSize = new Size(420, 155);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Label instructions = Ui.Label("Release all keys and mouse buttons, then hold the one you want to use. Esc cancels.", 388, 52);
            instructions.Location = new Point(16, 16);
            Controls.Add(instructions);
            hint = Ui.Label("Waiting for all buttons to be released...", 388, 68);
            hint.Location = new Point(16, 78);
            hint.ForeColor = Ui.Text;
            Controls.Add(hint);
            timer.Interval = 15;
            timer.Tick += Listen;
            Shown += OnShown;
        }

        // Begin polling only after the dialog is visible and able to receive focus.
        private void OnShown(object sender, EventArgs e)
        {
            timer.Start();
        }

        // The high-bit key state distinguishes a held button from a past press.
        private void Listen(object sender, EventArgs e)
        {
            if (WindowsApi.GetForegroundWindow() != Handle)
            {
                ready = false;
                SelectedKey = 0;
                hint.Text = "Return to this window to choose a button.";
                return;
            }
            if (WindowsApi.KeyIsDown((int)Keys.Escape))
            {
                CancelBinding();
                return;
            }
            if (SelectedKey != 0)
            {
                // Finish on release, so the binding press cannot activate a scan or dialog button.
                if (!WindowsApi.KeyIsDown(SelectedKey))
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
                return;
            }
            int pressed = 0;
            for (int key = 1; key <= 254; key++)
            {
                if (ActivationKey.CanListenFor(key) && WindowsApi.KeyIsDown(key))
                {
                    pressed = key;
                    break;
                }
            }
            if (pressed == 0)
            {
                if (!ready)
                    hint.Text = "Hold a key or mouse button now.\nMouse side buttons are supported. F6-F9 are reserved.";
                ready = true;
                return;
            }
            if (!ready)
                return;
            string error = ActivationKey.Validate(pressed, sendClicks);
            if (error != null)
            {
                hint.Text = error;
                ready = false;
                return;
            }
            SelectedKey = pressed;
            hint.Text = ActivationKey.Name(pressed) + " selected. Release it to finish.";
        }

        // Consume dialog navigation keys so Enter, Tab and arrows can also be bindings.
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Escape)
                CancelBinding();
            return true;
        }

        // F7 and Esc leave the previous binding unchanged.
        public void CancelBinding()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        // A closed dialog must stop reading button state.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Stop();
                timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
