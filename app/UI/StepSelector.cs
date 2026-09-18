using System.Drawing.Drawing2D;

namespace GHelper.UI
{
    /// <summary>
    /// A row of numbered segments, one selected - for settings that only have a few discrete
    /// levels (keyboard brightness is 0-3 in hardware), where a slider would suggest a continuous
    /// range that doesn't exist. Arrow keys step through the values when focused.
    /// </summary>
    public class StepSelector : Control
    {
        int minimum;
        int maximum = 3;
        int value;
        int hover = -1;

        public event EventHandler? ValueChanged;

        public int Minimum
        {
            get => minimum;
            set { minimum = value; Value = Math.Max(this.value, value); Invalidate(); }
        }

        public int Maximum
        {
            get => maximum;
            set { maximum = value; Value = Math.Min(this.value, value); Invalidate(); }
        }

        public int Value
        {
            get => value;
            set
            {
                int clamped = Math.Clamp(value, minimum, maximum);
                if (clamped == this.value) return;
                this.value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Sets the value without raising ValueChanged - for loading saved state.</summary>
        public void SetValueSilently(int newValue)
        {
            value = Math.Clamp(newValue, minimum, maximum);
            Invalidate();
        }

        int Count => maximum - minimum + 1;

        public StepSelector()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        RectangleF SegmentRect(int index)
        {
            float gap = Math.Max(3f, Height * 0.12f);
            float width = (Width - gap * (Count - 1) - 1) / Count;
            return new RectangleF(index * (width + gap), 0.5f, width, Height - 1.5f);
        }

        int IndexAt(Point p)
        {
            for (int i = 0; i < Count; i++)
                if (SegmentRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int index = IndexAt(e.Location);
            if (index >= 0) Value = minimum + index;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            if (index != hover) { hover = index; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != -1) { hover = -1; Invalidate(); }
        }

        protected override bool IsInputKey(Keys keyData)
            => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) Value--;
            else if (e.KeyCode == Keys.Right) Value++;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? RForm.formBack);

            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            float radius = Math.Max(3f, Height * 0.18f);

            for (int i = 0; i < Count; i++)
            {
                var rect = SegmentRect(i);
                bool selected = minimum + i == value;
                bool hovered = i == hover && !selected;

                Color fill = selected ? RForm.colorStandard
                    : hovered ? ControlPaint.Light(RForm.buttonMain, 0.15f)
                    : RForm.buttonMain;
                Color text = selected ? Color.White : RForm.foreMain;

                using (var path = Rounded(rect, radius))
                {
                    using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                    if (!selected)
                    {
                        using var edge = new Pen(RForm.borderMain == Color.Empty ? Color.FromArgb(90, 90, 96) : RForm.borderMain);
                        g.DrawPath(edge, path);
                    }
                }

                using var textBrush = new SolidBrush(text);
                g.DrawString((minimum + i).ToString(), Font, textBrush, rect, format);
            }

            if (Focused)
            {
                var r = SegmentRect(value - minimum);
                r.Inflate(-2, -2);
                ControlPaint.DrawFocusRectangle(g, Rectangle.Round(r));
            }
        }

        static GraphicsPath Rounded(RectangleF r, float radius)
        {
            radius = Math.Max(0.5f, Math.Min(radius, Math.Min(r.Width, r.Height) / 2f - 0.5f));
            float d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
