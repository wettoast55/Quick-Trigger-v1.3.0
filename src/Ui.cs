using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PopQuiz
{
    // Small factories keep repetitive styling out of the form's behavior code.
    internal static class Ui
    {
        public static readonly Color Background = Color.FromArgb(246, 248, 251);
        public static readonly Color Text = Color.FromArgb(28, 39, 55);
        public static readonly Color Muted = Color.FromArgb(94, 111, 134);
        public static readonly Color Accent = Color.FromArgb(39, 99, 226);

        // Buttons use ordinary event handlers and a consistent height.
        public static Button Button(string text, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.Height = 32;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(212, 220, 231);
            button.BackColor = Color.White;
            button.ForeColor = Text;
            button.Click += handler;
            return button;
        }

        // Names are also exposed to accessibility tools and screen readers.
        public static NumericUpDown Number(string name, int minimum, int maximum, int value)
        {
            NumericUpDown input = new NumericUpDown();
            input.Name = name.Replace(" ", "");
            input.AccessibleName = name;
            input.Minimum = minimum;
            input.Maximum = maximum;
            input.Value = value;
            input.ForeColor = Text;
            input.Width = 142;
            return input;
        }

        // Two labeled controls occupy one compact row.
        public static Panel Pair(string firstLabel, Control first, string secondLabel, Control second)
        {
            Panel row = new Panel();
            row.Size = new Size(302, 54);
            row.Margin = new Padding(0);
            AddField(row, firstLabel, first, 0);
            AddField(row, secondLabel, second, 160);
            return row;
        }

        // Keep labels above inputs so long values have enough horizontal space.
        private static void AddField(Panel row, string text, Control input, int x)
        {
            Label label = Label(text, 142, 20);
            label.Location = new Point(x, 0);
            input.Location = new Point(x, 22);
            input.Width = 142;
            row.Controls.Add(label);
            row.Controls.Add(input);
        }

        // Reuse this for hints, section labels and live status text.
        public static Label Label(string text, int width, int height)
        {
            Label label = new Label();
            label.Text = text;
            label.Size = new Size(width, height);
            label.ForeColor = Muted;
            label.Margin = new Padding(0);
            return label;
        }

        // Use a horizontal flow panel for short actions without absolute form coordinates.
        public static FlowLayoutPanel Buttons(params Button[] buttons)
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.Size = new Size(302, 38);
            row.Margin = new Padding(0);
            row.WrapContents = false;
            foreach (Button button in buttons)
            {
                button.Width = (302 - 6 * buttons.Length) / buttons.Length;
                button.Margin = new Padding(0, 0, 6, 0);
                row.Controls.Add(button);
            }
            return row;
        }
    }

    // The preview owns only UI copies; the scanner retains its separate capture bitmap.
    internal sealed class FramePreview : Panel
    {
        private Bitmap image;

        // Double buffering prevents flicker when new previews arrive.
        public FramePreview()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(20, 28, 41);
        }

        // Ownership transfers here. Dispose the previous frame before replacing it.
        public void SetFrame(Bitmap frame)
        {
            if (image != null)
                image.Dispose();
            image = frame;
            Invalidate();
        }

        // Preserve aspect ratio and use nearest-neighbor scaling to show individual pixels.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (image == null)
            {
                TextRenderer.DrawText(e.Graphics, "Classroom preview", Font, ClientRectangle, Color.SlateGray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            float scale = Math.Min((float)Width / image.Width, (float)Height / image.Height);
            int width = (int)(image.Width * scale);
            int height = (int)(image.Height * scale);
            Rectangle destination = new Rectangle((Width - width) / 2, (Height - height) / 2, width, height);
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(image, destination, new Rectangle(Point.Empty, image.Size), GraphicsUnit.Pixel);
        }

        // Release the last displayed bitmap when the form closes.
        protected override void Dispose(bool disposing)
        {
            if (disposing && image != null)
            {
                image.Dispose();
                image = null;
            }
            base.Dispose(disposing);
        }
    }
}
