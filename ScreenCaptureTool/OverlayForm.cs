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
        private static readonly object OpenSync = new object();
        private static readonly List<OverlayForm> OpenOverlays = new List<OverlayForm>();
        private static int CaptureSequence = 0;
        private static readonly Color[] MarkerPalette = new[]
        {
            Color.Red,
            Color.DeepSkyBlue,
            Color.LimeGreen,
            Color.Gold
        };
        private Bitmap image;
        private Settings settings;
        private readonly Color markerColor;
        private System.Drawing.Point lastMousePos;
        private bool isDragging = false;
        private bool isResizing = false;
        private string resizeDir = "";
        private List<MarkerForm> markers = new List<MarkerForm>();
        private List<Rectangle> markerRects = new List<Rectangle>();
        private readonly System.Drawing.Size baseSize;

        public OverlayForm(Bitmap img, System.Drawing.Rectangle region, Settings settings)
        {
            this.image = img;
            this.settings = settings;
            int seq = System.Threading.Interlocked.Increment(ref CaptureSequence);
            markerColor = MarkerPalette[(seq - 1) % MarkerPalette.Length];
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = region.Location;
            this.Size = new System.Drawing.Size(region.Width + settings.BorderSize * 2, region.Height + settings.BorderSize * 2);
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.DoubleBuffered = true;
            this.Opacity = settings.DefaultOpacity / 100.0;
            this.ContextMenuStrip = new ContextMenuStrip();
            baseSize = this.Size;

            this.MouseDown += OverlayForm_MouseDown;
            this.MouseMove += OverlayForm_MouseMove;
            this.MouseUp += OverlayForm_MouseUp;
            this.MouseWheel += OverlayForm_MouseWheel;
            this.KeyDown += OverlayForm_KeyDown;

            lock (OpenSync)
            {
                OpenOverlays.Add(this);
            }
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
            Rectangle contentRect = new Rectangle(
                settings.BorderSize,
                settings.BorderSize,
                this.Width - settings.BorderSize * 2,
                this.Height - settings.BorderSize * 2);
            if (contentRect.Width > 0 && contentRect.Height > 0)
            {
                double scaleX = contentRect.Width / (double)image.Width;
                double scaleY = contentRect.Height / (double)image.Height;
                double scale = Math.Min(scaleX, scaleY);
                int drawW = Math.Max(1, (int)Math.Round(image.Width * scale));
                int drawH = Math.Max(1, (int)Math.Round(image.Height * scale));
                int drawX = contentRect.X + (contentRect.Width - drawW) / 2;
                int drawY = contentRect.Y + (contentRect.Height - drawH) / 2;
                Rectangle destRect = new Rectangle(drawX, drawY, drawW, drawH);

                if (Math.Abs(scale - 1.0) < 0.001)
                {
                    g.DrawImageUnscaled(image, destRect.Location);
                }
                else
                {
                    g.DrawImage(image, destRect);
                }
            }
            
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
                double scaleX = e.X / (double)Math.Max(1, baseSize.Width);
                double scaleY = e.Y / (double)Math.Max(1, baseSize.Height);
                double scale = Math.Max(0.2, Math.Min(5.0, Math.Min(scaleX, scaleY)));
                int newW = Math.Max(20, (int)Math.Round(baseSize.Width * scale));
                int newH = Math.Max(20, (int)Math.Round(baseSize.Height * scale));
                this.Size = new System.Drawing.Size(newW, newH);
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
            if (IsCancelHotkey(e.KeyData))
            {
                CloseAllOpen();
            }
            else if (e.KeyCode == Keys.Enter)
            {
                StartMatching();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                CloseAllMarkers();
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_CONTEXTMENU = 0x007B;
            const int WM_RBUTTONUP = 0x0205;
            if (m.Msg == WM_CONTEXTMENU || m.Msg == WM_RBUTTONUP)
            {
                return;
            }
            base.WndProc(ref m);
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
                        count = MatchWithTemplate(screenBmp, settings.SimilarityThresholdPercent / 100.0);
                    }
                    if (count == 0)
                    {
                        // 找不到，在当前窗口中心显示红色 X
                        markers.Add(new MarkerForm(new Rectangle(this.Left + this.Width / 2 - 30, this.Top + this.Height / 2 - 30, 60, 60), "X", settings, markerColor));
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
                                    Rectangle rect = new Rectangle((int)minX, (int)minY, (int)Math.Max(1, maxX - minX), (int)Math.Max(1, maxY - minY));
                                    AddNumberedMarker(rect);
                                    return 1;
                                }

                                int targetW = Math.Max(8, (int)Math.Round(templateGray.Width * scale));
                                int targetH = Math.Max(8, (int)Math.Round(templateGray.Height * scale));
                                using (Mat scaledTemplate = new Mat())
                                {
                                    Cv2.Resize(templateGray, scaledTemplate, new OpenCvSharp.Size(targetW, targetH), 0, 0, InterpolationFlags.Linear);
                                    double threshold = settings.SimilarityThresholdPercent / 100.0;
                                    double relaxed = Math.Max(0.5, threshold - 0.02);
                                    int count = MatchWithTemplate(screenGray, scaledTemplate, relaxed);
                                    if (count > 0)
                                    {
                                        return count;
                                    }
                                }

                                Rectangle singleRect = new Rectangle((int)minX, (int)minY, (int)Math.Max(1, maxX - minX), (int)Math.Max(1, maxY - minY));
                                AddNumberedMarker(singleRect);
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
                        Rectangle rect = new Rectangle(maxLoc.X, maxLoc.Y, templateGray.Width, templateGray.Height);
                        AddNumberedMarker(rect);
                        Cv2.FloodFill(result, maxLoc, new Scalar(0));
                    }
                    else break;
                }

                return count;
            }
        }

        private void AddNumberedMarker(Rectangle rect)
        {
            if (IsOverlapping(rect))
            {
                return;
            }
            int id = markers.Count + 1;
            markers.Add(new MarkerForm(rect, id.ToString(), settings, markerColor));
            markerRects.Add(rect);
        }

        private bool IsOverlapping(Rectangle rect)
        {
            for (int i = 0; i < markerRects.Count; i++)
            {
                Rectangle existing = markerRects[i];
                Rectangle inter = Rectangle.Intersect(existing, rect);
                if (inter.Width <= 0 || inter.Height <= 0)
                {
                    continue;
                }
                double interArea = inter.Width * inter.Height;
                double minArea = Math.Min(existing.Width * existing.Height, rect.Width * rect.Height);
                if (minArea > 0 && (interArea / minArea) >= 0.6)
                {
                    return true;
                }
            }
            return false;
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
            markerRects.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CloseAllMarkers();
                image?.Dispose();
            }
            lock (OpenSync)
            {
                OpenOverlays.Remove(this);
            }
            base.Dispose(disposing);
        }

        public static void CloseAllOpen()
        {
            List<OverlayForm> overlays;
            lock (OpenSync)
            {
                overlays = new List<OverlayForm>(OpenOverlays);
            }
            foreach (var overlay in overlays)
            {
                overlay.CloseAllMarkers();
                overlay.Close();
            }
            MarkerForm.CloseAllOpen();
        }

        private bool IsCancelHotkey(Keys keyData)
        {
            Keys expected = (Keys)settings.CancelHotkeyCode;
            if ((settings.CancelHotkeyModifiers & 1) != 0) expected |= Keys.Alt;
            if ((settings.CancelHotkeyModifiers & 2) != 0) expected |= Keys.Control;
            if ((settings.CancelHotkeyModifiers & 4) != 0) expected |= Keys.Shift;
            return keyData == expected;
        }
    }

    public class MarkerForm : Form
    {
        private static readonly object OpenSync = new object();
        private static readonly List<MarkerForm> OpenMarkers = new List<MarkerForm>();
        private readonly string text;
        private readonly Settings settings;
        private readonly Color baseColor;

        public MarkerForm(Rectangle rect, string text, Settings settings, Color baseColor)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new System.Drawing.Size(Math.Max(20, rect.Width), Math.Max(20, rect.Height));
            this.Location = new System.Drawing.Point(rect.Left, rect.Top);
            this.StartPosition = FormStartPosition.Manual;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.BackColor = Color.Lime;
            this.TransparencyKey = Color.Lime;
            this.DoubleBuffered = true;
            this.ContextMenuStrip = new ContextMenuStrip();
            this.text = text;
            this.settings = settings;
            this.baseColor = baseColor;

            lock (OpenSync)
            {
                OpenMarkers.Add(this);
            }
            this.Show();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (Pen pen = new Pen(Color.FromArgb(settings.MarkerFillAlpha, baseColor), settings.MarkerBorderThickness))
            using (Font font = new Font("Arial", settings.MarkerFontSize, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(settings.MarkerFillAlpha, Color.White)))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
                g.DrawRectangle(pen, rect);
                g.DrawString(text, font, textBrush, rect, format);
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_CONTEXTMENU = 0x007B;
            const int WM_RBUTTONUP = 0x0205;
            const int WM_NCRBUTTONDOWN = 0x00A4;
            if (m.Msg == WM_CONTEXTMENU || m.Msg == WM_RBUTTONUP || m.Msg == WM_NCRBUTTONDOWN)
            {
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnClosed(EventArgs e)
        {
            lock (OpenSync)
            {
                OpenMarkers.Remove(this);
            }
            base.OnClosed(e);
        }

        public static void CloseAllOpen()
        {
            List<MarkerForm> markers;
            lock (OpenSync)
            {
                markers = new List<MarkerForm>(OpenMarkers);
            }
            foreach (var marker in markers)
            {
                marker.Close();
            }
        }
    }
}
