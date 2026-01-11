using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using OpenCvSharp;
using OpenCvSharp.Extensions; // Note: Ensure OpenCvSharp4.Extensions package is added if needed, or use BitmapConverter directly

namespace ScreenCaptureTool
{
    public class OverlayForm : Form
    {
        private Bitmap image;
        private Rectangle region;
        private Settings settings;
        private List<Rectangle> matchResults = new List<Rectangle>();
        private bool isDragging = false;
        private System.Drawing.Point dragStart;

        public OverlayForm(Bitmap img, Rectangle reg, Settings sets)
        {
            this.image = img;
            this.region = reg;
            this.settings = sets;

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.Opacity = settings.DefaultOpacity;
            this.Size = img.Size;
            this.Location = reg.Location;
            this.BackgroundImage = img;
            this.BackgroundImageLayout = ImageLayout.Stretch;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                isDragging = true;
                dragStart = e.Location;
            }
            else if (e.Button == MouseButtons.Right && Control.ModifierKeys == Keys.Control)
            {
                this.Close();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (isDragging)
            {
                this.Left += e.X - dragStart.X;
                this.Top += e.Y - dragStart.Y;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            isDragging = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Control.ModifierKeys == Keys.Control)
            {
                // 缩放
                float scale = e.Delta > 0 ? 1.1f : 0.9f;
                this.Width = (int)(this.Width * scale);
                this.Height = (int)(this.Height * scale);
            }
            else
            {
                // 透明度
                this.Opacity = Math.Clamp(this.Opacity + (e.Delta > 0 ? 0.05 : -0.05), 0.1, 1.0);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                PerformImageMatch();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                matchResults.Clear();
                this.Invalidate();
            }
        }

        private void PerformImageMatch()
        {
            // 截取全屏
            Bitmap screen = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
            using (var g = Graphics.FromImage(screen))
            {
                g.CopyFromScreen(0, 0, 0, 0, screen.Size);
            }

            using (Mat screenMat = BitmapConverter.ToMat(screen))
            using (Mat templateMat = BitmapConverter.ToMat(image))
            using (Mat res = new Mat())
            {
                Cv2.MatchTemplate(screenMat, templateMat, res, TemplateMatchModes.CCoeffNormed);
                Cv2.Threshold(res, res, 0.8, 1.0, ThresholdTypes.Tozero);

                matchResults.Clear();
                while (true)
                {
                    double minVal, maxVal;
                    OpenCvSharp.Point minLoc, maxLoc;
                    Cv2.MinMaxLoc(res, out minVal, out maxVal, out minLoc, out maxLoc);

                    if (maxVal >= 0.8)
                    {
                        matchResults.Add(new Rectangle(maxLoc.X, maxLoc.Y, image.Width, image.Height));
                        // 屏蔽已找到的区域
                        Cv2.FloodFill(res, maxLoc, new Scalar(0));
                    }
                    else break;
                }
            }
            
            // 在屏幕上显示标记（这里需要一个全屏透明层来绘制标记，简化起见直接在当前 Overlay 逻辑中处理或弹出新层）
            ShowMarkers();
        }

        private void ShowMarkers()
        {
            // 逻辑：创建一个全屏透明窗体来绘制 1, 2 标记
            var markerForm = new MarkerForm(matchResults);
            markerForm.Show();
        }
    }

    public class MarkerForm : Form
    {
        private List<Rectangle> markers;
        public MarkerForm(List<Rectangle> m)
        {
            this.markers = m;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.TransparencyKey = Color.Magenta;
            this.BackColor = Color.Magenta;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                var rect = markers[i];
                e.Graphics.DrawRectangle(Pens.Red, rect);
                e.Graphics.DrawString((i + 1).ToString(), new Font("Arial", 16), Brushes.Yellow, rect.Location);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) this.Close();
        }
    }
}
