using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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
                    int count = TryMatchWithOrb(screenBmp);
                    if (count == 0)
                    {
                        count = MatchWithTemplate(screenBmp, 0.9);
                    }
                    if (count == 0)
                    {
                        // 找不到，在当前窗口中心显示红色 X
                        markers.Add(new MarkerForm("X", new System.Drawing.Point(this.Left + this.Width / 2, this.Top + this.Height / 2), Color.Red));
                    }
                }
            }
            catch (Exception ex)
            {
                string logPath = LogException(ex);
                MessageBox.Show("匹配出错: " + ex.Message + "\n日志: " + logPath);
            }
        }

        private int TryMatchWithOrb(Bitmap screenBmp)
        {
            using (Mat screenMat = screenBmp.ToMat())
            using (Mat templateMat = image.ToMat())
            using (Mat screenGray = new Mat())
            using (Mat templateGray = new Mat())
            {
                Cv2.CvtColor(screenMat, screenGray, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(templateMat, templateGray, ColorConversionCodes.BGR2GRAY);

                using (var orb = ORB.Create(1000))
                {
                    KeyPoint[] kpTemplate, kpScreen;
                    using (Mat descTemplate = new Mat())
                    using (Mat descScreen = new Mat())
                    {
                        orb.DetectAndCompute(templateGray, null, out kpTemplate, descTemplate);
                        orb.DetectAndCompute(screenGray, null, out kpScreen, descScreen);

                        if (descTemplate.Empty() || descScreen.Empty())
                        {
                            return 0;
                        }

                        using (var matcher = new BFMatcher(NormTypes.Hamming, false))
                        {
                            DMatch[][] knnMatches = matcher.KnnMatch(descTemplate, descScreen, 2);
                            List<DMatch> good = new List<DMatch>();
                            for (int i = 0; i < knnMatches.Length; i++)
                            {
                                if (knnMatches[i].Length < 2) continue;
                                if (knnMatches[i][0].Distance < 0.75f * knnMatches[i][1].Distance)
                                {
                                    good.Add(knnMatches[i][0]);
                                }
                            }

                            if (good.Count < 12)
                            {
                                return 0;
                            }

                            Point2f[] srcPts = new Point2f[good.Count];
                            Point2f[] dstPts = new Point2f[good.Count];
                            for (int i = 0; i < good.Count; i++)
                            {
                                srcPts[i] = kpTemplate[good[i].QueryIdx].Pt;
                                dstPts[i] = kpScreen[good[i].TrainIdx].Pt;
                            }

                            using (Mat homography = Cv2.FindHomography(InputArray.Create(srcPts), InputArray.Create(dstPts), HomographyMethods.Ransac, 3.0))
                            {
                                if (homography.Empty())
                                {
                                    return 0;
                                }

                                Point2f[] corners = new Point2f[]
                                {
                                    new Point2f(0, 0),
                                    new Point2f(templateMat.Width, 0),
                                    new Point2f(templateMat.Width, templateMat.Height),
                                    new Point2f(0, templateMat.Height)
                                };
                                Point2f[] projected = Cv2.PerspectiveTransform(corners, homography);

                                double minX = projected[0].X;
                                double maxX = projected[0].X;
                                double minY = projected[0].Y;
                                double maxY = projected[0].Y;
                                for (int i = 1; i < projected.Length; i++)
                                {
                                    if (projected[i].X < minX) minX = projected[i].X;
                                    if (projected[i].X > maxX) maxX = projected[i].X;
                                    if (projected[i].Y < minY) minY = projected[i].Y;
                                    if (projected[i].Y > maxY) maxY = projected[i].Y;
                                }

                                double width = Distance(projected[0], projected[1]);
                                double height = Distance(projected[0], projected[3]);
                                double scaleX = width / Math.Max(1, templateMat.Width);
                                double scaleY = height / Math.Max(1, templateMat.Height);
                                double scale = (scaleX + scaleY) * 0.5;

                                if (scale < 0.2 || scale > 5.0)
                                {
                                    // 缩放异常时，仅标记一次
                                    System.Drawing.Point center = new System.Drawing.Point((int)((minX + maxX) * 0.5), (int)((minY + maxY) * 0.5));
                                    AddNumberedMarker(center);
                                    return 1;
                                }

                                int targetW = Math.Max(8, (int)Math.Round(templateGray.Width * scale));
                                int targetH = Math.Max(8, (int)Math.Round(templateGray.Height * scale));
                                using (Mat scaledTemplate = new Mat())
                                {
                                    Cv2.Resize(templateGray, scaledTemplate, new OpenCvSharp.Size(targetW, targetH), 0, 0, InterpolationFlags.Linear);
                                    int count = MatchWithTemplate(screenGray, scaledTemplate, 0.88);
                                    if (count > 0)
                                    {
                                        return count;
                                    }
                                }

                                System.Drawing.Point singleCenter = new System.Drawing.Point((int)((minX + maxX) * 0.5), (int)((minY + maxY) * 0.5));
                                AddNumberedMarker(singleCenter);
                                return 1;
                            }
                        }
                    }
                }
            }
        }

        private int MatchWithTemplate(Bitmap screenBmp, double threshold)
        {
            using (Mat screenMat = screenBmp.ToMat())
            using (Mat templateMat = image.ToMat())
            using (Mat screenGray = new Mat())
            using (Mat templateGray = new Mat())
            {
                Cv2.CvtColor(screenMat, screenGray, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(templateMat, templateGray, ColorConversionCodes.BGR2GRAY);
                return MatchWithTemplate(screenGray, templateGray, threshold);
            }
        }

        private int MatchWithTemplate(Mat screenGray, Mat templateGray, double threshold)
        {
            if (screenGray.Width < templateGray.Width || screenGray.Height < templateGray.Height)
            {
                return 0;
            }

            using (Mat result = new Mat())
            {
                Cv2.MatchTemplate(screenGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
                Cv2.Threshold(result, result, threshold, 1.0, ThresholdTypes.Tozero);

                int count = 0;
                while (true)
                {
                    double minVal, maxVal;
                    OpenCvSharp.Point minLoc, maxLoc;
                    Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);

                    if (maxVal >= threshold && count < 50) // 最多标记 50 个
                    {
                        count++;
                        System.Drawing.Point pos = new System.Drawing.Point(maxLoc.X + templateGray.Width / 2, maxLoc.Y + templateGray.Height / 2);
                        AddNumberedMarker(pos);
                        Cv2.FloodFill(result, maxLoc, new Scalar(0));
                    }
                    else break;
                }

                return count;
            }
        }

        private void AddNumberedMarker(System.Drawing.Point pos)
        {
            int id = markers.Count + 1;
            markers.Add(new MarkerForm(id.ToString(), pos, Color.Red));
        }

        private static double Distance(Point2f a, Point2f b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string LogException(Exception ex)
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "match_error.log");
            try
            {
                File.AppendAllText(logPath, DateTime.Now.ToString("u") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
                // Ignore logging failures to avoid masking original error.
            }
            return logPath;
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
