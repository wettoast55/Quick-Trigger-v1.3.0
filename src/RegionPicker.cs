using System;
using System.Drawing;
using System.Windows.Forms;

namespace PopQuiz
{
        // A temporary overlay lets the user draw a rectangle on one selected display.
    internal sealed class RegionPicker : Form
    {
        private Point startPoint;
        private Rectangle selection;
        private bool dragging;
        public Rectangle SelectedRegion { get; private set; }

        // The overlay closes before any captured pixels are analyzed.
        public RegionPicker(Rectangle displayBounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = displayBounds;
            BackColor = Color.Black;
            Opacity = 0.40;
            TopMost = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            KeyPreview = true;
        }

        // Escape cancels without changing the saved scan area.
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        // A left-button drag starts a selection; a right-click cancels it.
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            if (e.Button != MouseButtons.Left)
                return;
            startPoint = e.Location;
            dragging = true;
            Capture = true;
        }

        // Normalize the corners so dragging in any direction works.
        private void UpdateSelection(Point pointer)
        {
            // Mouse capture can report positions beyond this monitor; clamp the final rectangle.
            pointer.X = Math.Max(0, Math.Min(ClientSize.Width, pointer.X));
            pointer.Y = Math.Max(0, Math.Min(ClientSize.Height, pointer.Y));
            selection = Rectangle.FromLTRB(
                Math.Min(startPoint.X, pointer.X), Math.Min(startPoint.Y, pointer.Y),
                Math.Max(startPoint.X, pointer.X), Math.Max(startPoint.Y, pointer.Y));
            Invalidate();
        }

        // Repaint only while the user is drawing the rectangle.
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
                UpdateSelection(e.Location);
        }

        // Convert the final local rectangle to physical desktop coordinates.
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left)
                return;
            UpdateSelection(e.Location);
            dragging = false;
            Capture = false;
            if (selection.Width < 1 || selection.Height < 1)
                return;
            SelectedRegion = new Rectangle(PointToScreen(selection.Location), selection.Size);
            DialogResult = DialogResult.OK;
            Close();
        }

        // A border and a short instruction are the only overlay decorations.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(Color.White, 3))
                e.Graphics.DrawRectangle(border, selection);
            Point pointer = PointToClient(Cursor.Position);
            using (Font font = new Font("Segoe UI", 14, FontStyle.Bold))
                e.Graphics.DrawString("Mark your chalkboard  |  Esc cancels", font, Brushes.White, pointer.X + 20, pointer.Y + 20);
        }
    }
}
