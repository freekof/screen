using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace ScreenCaptureTool
{
    public class OverlayForm : Form
    {
        private Bitmap image;
        private Settings settings;
        private System.Drawing.Point lastMousePos;
        private bool isDragging = false;
        private bool isResizing = false;
        private string resizeDir = "";
        private List<MarkerForm> markers = new List<MarkerForm>();

        public OverlayForm(Bitmap img, System.Drawing.Rectangle region, Settings settings)
        {
            this.image = img;
            this.settings = settings;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = region.Location;
            this.Size = new System.Drawing.Size(region.Width + settings.BorderSize * 2, region.Height + settings.BorderSize * 2);
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
            Graphics g = e.Graphics;
            // 画边框
            using (Pen pen = new Pen(Color.Cyan, settings.BorderSize))
            {
                g.DrawRectangle(pen, settings.BorderSize / 2, settings.BorderSize / 2, this.Width - settings.BorderSize, this.Height - settings.BorderSize);
            }
            // 画图片
            g.DrawImage(image, settings.BorderSize, settings.BorderSize, 
                this.Width - settings.BorderSize * 2, this.Height - settings.BorderSize * 2);
            
            // 画右下角缩放手柄提示
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(150, Color.Cyan)))
            {
                System.Drawing.Point[] pts = {
                    new System.Drawing.Point(this.Width, this.Height - 10),
                    new System.Drawing.Point(this.Width, this.Height),
                    new System.Drawing.Point(this.Width - 10, this.Height)
                };
                g.FillPolygon(brush, pts);
            }
        }

        private void OverlayForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (e.X > this.Width - 20 && e.Y > this.Height - 20) { isResizing = true; resizeDir = "corner"; }
                else { isDragging = true; lastMousePos = e.Location; }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (ModifierKeys == Keys.Control)
                {
                    CloseAllMarkers();
                    this.Close();
                }
            }
        }

        private void OverlayForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.X > this.Width - 20 && e.Y > this.Height - 20) this.Cursor = Cursors.SizeNWSE;
            else this.Cursor = Cursors.SizeAll;

            if (isResizing)
            {
                this.Size = new System.Drawing.Size(Math.Max(20, e.X), Math.Max(20, e.Y));
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
            if (ModifierKeys == Keys.Control)
            {
                float ratio = e.Delta > 0 ? 1.1f : 0.9f;
                this.Size = new System.Drawing.Size((int)(this.Width * ratio), (int)(this.Height * ratio));
            }
            else
            {
                double delta = e.Delta / 1200.0;
                this.Opacity = Math.Max(0.1, Math.Min(1.0, this.Opacity + delta));
            }
        }

        private void OverlayForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                StartMatching();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                CloseAllMarkers();
            }
        }

        private void StartMatching()
        {
            CloseAllMarkers();
            try
            {
                // 1. 截取全屏
                System.Drawing.Rectangle bounds = Screen.PrimaryScreen.Bounds;
                using (Bitmap screenBmp = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (Graphics g = Graphics.FromImage(screenBmp))
                    {
                        g.CopyFromScreen(0, 0, 0, 0, bounds.Size);
                    }

                    // 2. OpenCV 匹配
                    using (Mat screenMat = screenBmp.ToMat())
                    using (Mat templateMat = image.ToMat())
                    using (Mat result = new Mat())
                    {
                        Cv2.MatchTemplate(screenMat, templateMat, result, TemplateMatchModes.CCoeffNormed);
                        Cv2.Threshold(result, result, 0.9, 1.0, ThresholdTypes.Tozero);

                        int count = 0;
                        while (true)
                        {
                            double minVal, maxVal;
                            OpenCvSharp.Point minLoc, maxLoc;
                            Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);

                            if (maxVal >= 0.9 && count < 50) // 最多标记 50 个
                            {
                                count++;
                                System.Drawing.Point pos = new System.Drawing.Point(maxLoc.X + templateMat.Width / 2, maxLoc.Y + templateMat.Height / 2);
                                markers.Add(new MarkerForm(count.ToString(), pos, Color.Red));
                                Cv2.FloodFill(result, maxLoc, new Scalar(0));
                            }
                            else break;
                        }

                        if (count == 0)
                        {
                            // 找不到，在当前窗口中心显示红色 X
                            markers.Add(new MarkerForm("X", new System.Drawing.Point(this.Left + this.Width / 2, this.Top + this.Height / 2), Color.Red));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("匹配出错: " + ex.Message);
            }
        }

        private void CloseAllMarkers()
        {
            foreach (var m in markers) m.Close();
            markers.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CloseAllMarkers();
                image?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class MarkerForm : Form
    {
        public MarkerForm(string text, System.Drawing.Point pos, Color color)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new System.Drawing.Size(60, 60);
            this.Location = new System.Drawing.Point(pos.X - 30, pos.Y - 30);
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = Color.Lime;
            this.TransparencyKey = Color.Lime;

            Label lbl = new Label { 
                Text = text, 
                ForeColor = color, 
                Font = new Font("Arial", 24, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill
            };
            this.Controls.Add(lbl);
            this.Show();
        }
    }
}
