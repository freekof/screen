using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace ScreenCaptureTool
{
    public class OverlayForm : Form
    {
        private Bitmap image;
        private Settings settings;
        private Point lastMousePos;
        private bool isDragging = false;
        private bool isResizing = false;
        private string resizeDir = "";
        private List<MarkerForm> markers = new List<MarkerForm>();

        public OverlayForm(Bitmap img, Rectangle region, Settings settings)
        {
            this.image = img;
            this.settings = settings;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = region.Location;
            this.Size = new Size(region.Width + settings.BorderSize * 2, region.Height + settings.BorderSize * 2);
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;
            this.Opacity = settings.DefaultOpacity / 100.0;

            this.MouseDown += OverlayForm_MouseDown;
            this.MouseMove += OverlayForm_MouseMove;
            this.MouseUp += OverlayForm_MouseUp;
            this.MouseWheel += OverlayForm_MouseWheel;
            this.KeyDown += OverlayForm_KeyDown;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 画边框
            using (Pen pen = new Pen(Color.Red, settings.BorderSize))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, this.Width - 1, this.Height - 1);
            }
            // 画图片
            e.Graphics.DrawImage(image, settings.BorderSize, settings.BorderSize, 
                this.Width - settings.BorderSize * 2, this.Height - settings.BorderSize * 2);
        }

        private void OverlayForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (e.X > this.Width - 15 && e.Y > this.Height - 15) { isResizing = true; resizeDir = "corner"; }
                else if (e.X > this.Width - 10) { isResizing = true; resizeDir = "right"; }
                else if (e.Y > this.Height - 10) { isResizing = true; resizeDir = "bottom"; }
                else { isDragging = true; lastMousePos = e.Location; }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (ModifierKeys == Keys.Control || ModifierKeys == Keys.Shift || ModifierKeys == Keys.Alt)
                {
                    this.Close();
                }
            }
        }

        private void OverlayForm_MouseMove(object sender, MouseEventArgs e)
        {
            // 设置光标
            if (e.X > this.Width - 15 && e.Y > this.Height - 15) this.Cursor = Cursors.SizeNWSE;
            else if (e.X > this.Width - 10) this.Cursor = Cursors.SizeWE;
            else if (e.Y > this.Height - 10) this.Cursor = Cursors.SizeNS;
            else this.Cursor = Cursors.Arrow;

            if (isResizing)
            {
                if (resizeDir == "corner") this.Size = new Size(e.X, e.Y);
                else if (resizeDir == "right") this.Width = e.X;
                else if (resizeDir == "bottom") this.Height = e.Y;
                this.Invalidate();
            }
            else if (isDragging)
            {
                this.Left += e.X - lastMousePos.X;
                this.Top += e.Y - lastMousePos.Y;
            }
        }

        private void OverlayForm_MouseUp(object sender, MouseEventArgs e)
        {
            isDragging = false;
            isResizing = false;
        }

        private void OverlayForm_MouseWheel(object sender, MouseEventArgs e)
        {
            double delta = e.Delta / 1200.0;
            this.Opacity = Math.Max(0.1, Math.Min(1.0, this.Opacity + delta));
        }

        private void OverlayForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                StartMatching();
            }
        }

        private void StartMatching()
        {
            // 简单的图像匹配逻辑（在 C# 中通常使用 OpenCVSharp，这里提供逻辑框架）
            // 实际匹配建议在 Windows 下使用 OpenCVSharp 库
            MessageBox.Show("正在全屏查找匹配项... (此功能在 EXE 中将调用 OpenCV 逻辑)");
        }
    }

    public class MarkerForm : Form
    {
        public MarkerForm(string text, Point pos, Color color)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new Size(40, 40);
            this.Location = new Point(pos.X - 20, pos.Y - 20);
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = Color.Lime;
            this.TransparencyKey = Color.Lime;

            Label lbl = new Label { 
                Text = text, 
                ForeColor = color, 
                Font = new Font("Arial", 12, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            this.Controls.Add(lbl);
            this.Show();
        }
    }
}
