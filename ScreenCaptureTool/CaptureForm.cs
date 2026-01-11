using System;
using System.Drawing;
using System.Windows.Forms;

namespace ScreenCaptureTool
{
    public class CaptureForm : Form
    {
        public Bitmap SelectedImage { get; private set; }
        public Rectangle SelectedRegion { get; private set; }
        private Point startPoint;
        private Rectangle currentRect;
        private Bitmap screenSnapshot;

        public CaptureForm(Settings settings)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;
            this.TopMost = true;

            CaptureScreen();
        }

        private void CaptureScreen()
        {
            screenSnapshot = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
            using (var g = Graphics.FromImage(screenSnapshot))
            {
                g.CopyFromScreen(0, 0, 0, 0, screenSnapshot.Size);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                startPoint = e.Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int x = Math.Min(startPoint.X, e.X);
                int y = Math.Min(startPoint.Y, e.Y);
                int width = Math.Abs(startPoint.X - e.X);
                int height = Math.Abs(startPoint.Y - e.Y);
                currentRect = new Rectangle(x, y, width, height);
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && currentRect.Width > 0)
            {
                SelectedRegion = currentRect;
                SelectedImage = screenSnapshot.Clone(currentRect, screenSnapshot.PixelFormat);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 绘制变暗背景
            e.Graphics.DrawImage(screenSnapshot, 0, 0);
            using (var brush = new SolidBrush(Color.FromArgb(120, Color.Black)))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }

            // 绘制选择区域（正常显示）
            if (currentRect.Width > 0)
            {
                e.Graphics.SetClip(currentRect);
                e.Graphics.DrawImage(screenSnapshot, 0, 0);
                e.Graphics.ResetClip();
                e.Graphics.DrawRectangle(Pens.Red, currentRect);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }
    }
}
