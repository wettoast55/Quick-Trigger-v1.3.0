using System;
using System.Drawing;
using System.Windows.Forms;

namespace ColorTriggerLab
{
    // The selection overlay exists only while choosing a rectangle; it is closed
    // before capture starts so its colors cannot contaminate the detector.
    internal sealed class RegionPicker : Form
    {
        private Point anchor;
        private Rectangle selection;
        private bool dragging;
        public Rectangle SelectedRegion { get; private set; }

        public RegionPicker()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            BackColor = Color.Black;
            Opacity = 0.40;
            TopMost = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
            anchor = e.Location; dragging = true; Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            selection = Rectangle.FromLTRB(Math.Min(anchor.X, e.X), Math.Min(anchor.Y, e.Y), Math.Max(anchor.X, e.X), Math.Max(anchor.Y, e.Y));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false; Capture = false;
            if (selection.Width < 1 || selection.Height < 1) return;
            SelectedRegion = new Rectangle(PointToScreen(selection.Location), selection.Size);
            DialogResult = DialogResult.OK; Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(Color.White, 3)) e.Graphics.DrawRectangle(pen, selection);
            Point pointer = PointToClient(Cursor.Position);
            using (Font font = new Font("Segoe UI", 15, FontStyle.Bold))
                e.Graphics.DrawString("Drag a region  |  Esc cancels", font, Brushes.White, pointer.X + 20, pointer.Y + 20);
        }
    }
}
