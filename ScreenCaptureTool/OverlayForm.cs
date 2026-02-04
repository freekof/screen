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
        private Bitmap image;
        private Settings settings;
        private Color currentMatchColor;
        private System.Drawing.Point lastMousePos;
        private bool isDragging = false;
        private bool isResizing = false;
        private bool suppressRightClick = false;
        private string resizeDir = "";
        private MarkerOverlayForm markerOverlay;
        private int markerCount = 0;
        private List<Rectangle> markerRects = new List<Rectangle>();
        private const int MinImageScale = 20;
        private const int MaxImageScale = 500;
        private const int MaxMatchCount = 50;
        private const double LowVarianceStdDev = 5.0;
        private int imageScale = 100;
        private float DpiScale => Math.Max(1.0f, this.DeviceDpi / 96f);

        private int ImageScale
        {
            get { return imageScale; }
            set
            {
                int newScale = Math.Max(MinImageScale, Math.Min(MaxImageScale, value));
                if (imageScale != newScale)
                {
                    imageScale = newScale;
                    AutoSizeForm();
                }
            }
        }

        private System.Drawing.Size ImageSize
        {
            get
            {
                float scale = ImageScale / 100f;
                return new System.Drawing.Size(
                    Math.Max(1, (int)Math.Round(image.Width * scale)),
                    Math.Max(1, (int)Math.Round(image.Height * scale)));
            }
        }

        private System.Drawing.Size FormSize
        {
            get
            {
                int border = settings.BorderSize * 2;
                System.Drawing.Size size = ImageSize;
                return new System.Drawing.Size(size.Width + border, size.Height + border);
            }
        }

        public OverlayForm(Bitmap img, System.Drawing.Rectangle region, Settings settings)
        {
            this.image = img;
            this.settings = settings;
            currentMatchColor = MarkerColorProvider.NextSharedColor();
            this.AutoScaleMode = AutoScaleMode.None;
            this.AutoSize = false;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ControlBox = false;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowIcon = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = region.Location;
            ImageScale = 100;
            this.Size = FormSize;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.AutoScaleMode = AutoScaleMode.None;
            this.WindowState = FormWindowState.Normal;
            this.DoubleBuffered = true;
            this.Opacity = settings.DefaultOpacity / 100.0;
            this.ContextMenuStrip = new ContextMenuStrip();

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

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.Style &= ~0x00080000; // WS_SYSMENU
                cp.Style &= ~0x00020000; // WS_MINIMIZEBOX
                cp.Style &= ~0x00010000; // WS_MAXIMIZEBOX
                return cp;
            }
        }

        private void AutoSizeForm()
        {
            System.Drawing.Size newSize = FormSize;
            if (this.Size != newSize)
            {
                this.Size = newSize;
            }
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // 画边框
            Rectangle? drawnRect = null;
            // 画图片
            System.Drawing.Size drawSize = ImageSize;
            if (drawSize.Width > 0 && drawSize.Height > 0)
            {
                Rectangle destRect = new Rectangle(settings.BorderSize, settings.BorderSize, drawSize.Width, drawSize.Height);
                drawnRect = destRect;
                if (ImageScale == 100)
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.DrawImage(image, new Rectangle(destRect.Location, image.Size));
                }
                else
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(image, destRect);
                }
            }

            // 画边框（跟随图像区域，避免拉伸）
            if (drawnRect.HasValue)
            {
                using (Pen pen = new Pen(Color.Orange, settings.BorderSize))
                {
                    Rectangle rect = drawnRect.Value;
                    g.DrawRectangle(pen, rect);
                }
            }
            
            // 画右下角缩放手柄提示
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(150, Color.Cyan)))
            {
                if (drawnRect.HasValue)
                {
                    Rectangle rect = drawnRect.Value;
                    System.Drawing.Point[] pts = {
                        new System.Drawing.Point(rect.Right, rect.Bottom - 10),
                        new System.Drawing.Point(rect.Right, rect.Bottom),
                        new System.Drawing.Point(rect.Right - 10, rect.Bottom)
                    };
                    g.FillPolygon(brush, pts);
                }
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
                    suppressRightClick = true;
                    this.Capture = true;
                    return;
                }
            }
        }

        private void OverlayForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.X > this.Width - 20 && e.Y > this.Height - 20) this.Cursor = Cursors.SizeNWSE;
            else this.Cursor = Cursors.SizeAll;

            if (isResizing)
            {
                int border = settings.BorderSize * 2;
                int targetW = Math.Max(1, e.X - border);
                int targetH = Math.Max(1, e.Y - border);
                double scaleX = targetW / (double)Math.Max(1, image.Width);
                double scaleY = targetH / (double)Math.Max(1, image.Height);
                int targetScale = (int)Math.Round(Math.Min(scaleX, scaleY) * 100.0);
                ImageScale = targetScale;
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

            if (e.Button == MouseButtons.Right && suppressRightClick)
            {
                var closeTimer = new System.Windows.Forms.Timer { Interval = 50 };
                closeTimer.Tick += (s, args) =>
                {
                    closeTimer.Stop();
                    closeTimer.Dispose();
                    suppressRightClick = false;
                    this.Capture = false;
                    CloseAllMarkers();
                    this.Close();
                };
                closeTimer.Start();
            }
        }

        private void OverlayForm_MouseWheel(object sender, MouseEventArgs e)
        {
            if (ModifierKeys == Keys.Control)
            {
                int delta = e.Delta > 0 ? 10 : -10;
                ImageScale += delta;
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
            else if (e.KeyCode == Keys.Up)
            {
                this.Top -= 1;
            }
            else if (e.KeyCode == Keys.Down)
            {
                this.Top += 1;
            }
            else if (e.KeyCode == Keys.Left)
            {
                this.Left -= 1;
            }
            else if (e.KeyCode == Keys.Right)
            {
                this.Left += 1;
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
            const int WM_NCRBUTTONDOWN = 0x00A4;
            const int WM_NCRBUTTONUP = 0x00A5;
            if (m.Msg == WM_CONTEXTMENU ||
                (suppressRightClick && (m.Msg == WM_NCRBUTTONDOWN || m.Msg == WM_NCRBUTTONUP)))
            {
                return;
            }
            base.WndProc(ref m);
        }

        private void StartMatching()
        {
            CloseAllMarkers();
            currentMatchColor = MarkerColorProvider.NextSharedColor();
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
                        Rectangle rect = new Rectangle(this.Left + this.Width / 2 - 30, this.Top + this.Height / 2 - 30, 60, 60);
                        EnsureMarkerOverlay();
                        markerOverlay.AddMarker(rect, "X");
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
                ConvertToGray(screenMat, screenGray);
                ConvertToGray(templateMat, templateGray);

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
                ConvertToGray(screenMat, screenGray);
                ConvertToGray(templateMat, templateGray);
                return MatchWithTemplate(screenGray, templateGray, threshold);
            }
        }

        private int MatchWithTemplate(Mat screenGray, Mat templateGray, double threshold)
        {
            if (screenGray.Width < templateGray.Width || screenGray.Height < templateGray.Height)
            {
                return 0;
            }
            int count = MatchWithTemplateAdaptive(screenGray, templateGray, threshold);
            if (count > 0)
            {
                return count;
            }

            double relaxed = Math.Max(0.5, threshold - 0.03);
            double[] scales = new[] { 0.9, 1.1, 0.8, 1.2, 0.7, 1.3, 0.6 };
            for (int i = 0; i < scales.Length; i++)
            {
                int targetW = (int)Math.Round(templateGray.Width * scales[i]);
                int targetH = (int)Math.Round(templateGray.Height * scales[i]);
                if (targetW < 8 || targetH < 8)
                {
                    continue;
                }
                if (targetW > screenGray.Width || targetH > screenGray.Height)
                {
                    continue;
                }

                using (Mat scaledTemplate = new Mat())
                {
                    Cv2.Resize(templateGray, scaledTemplate, new OpenCvSharp.Size(targetW, targetH), 0, 0, InterpolationFlags.Linear);
                    count = MatchWithTemplateAdaptive(screenGray, scaledTemplate, relaxed);
                    if (count > 0)
                    {
                        return count;
                    }
                }
            }

            return 0;
        }

        private int MatchWithTemplateAdaptive(Mat screenGray, Mat templateGray, double threshold)
        {
            bool lowVariance = IsLowVariance(templateGray);
            if (!lowVariance)
            {
                int count = MatchWithTemplateCCoeff(screenGray, templateGray, threshold);
                if (count > 0)
                {
                    return count;
                }
            }

            return MatchWithTemplateSqDiff(screenGray, templateGray, threshold);
        }

        private int MatchWithTemplateCCoeff(Mat screenGray, Mat templateGray, double threshold)
        {
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
                    if (double.IsNaN(maxVal))
                    {
                        return 0;
                    }

                    if (maxVal >= threshold && count < MaxMatchCount)
                    {
                        count++;
                        Rectangle rect = new Rectangle(maxLoc.X, maxLoc.Y, templateGray.Width, templateGray.Height);
                        AddNumberedMarker(rect);
                        Cv2.FloodFill(result, maxLoc, new Scalar(0));
                    }
                    else
                    {
                        break;
                    }
                }

                return count;
            }
        }

        private int MatchWithTemplateSqDiff(Mat screenGray, Mat templateGray, double threshold)
        {
            double diffThreshold = Math.Max(0.0, 1.0 - threshold);
            using (Mat result = new Mat())
            {
                Cv2.MatchTemplate(screenGray, templateGray, result, TemplateMatchModes.SqDiffNormed);

                int count = 0;
                while (true)
                {
                    double minVal, maxVal;
                    OpenCvSharp.Point minLoc, maxLoc;
                    Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);

                    if (minVal <= diffThreshold && count < MaxMatchCount)
                    {
                        count++;
                        Rectangle rect = new Rectangle(minLoc.X, minLoc.Y, templateGray.Width, templateGray.Height);
                        AddNumberedMarker(rect);
                        Cv2.FloodFill(result, minLoc, new Scalar(1.0));
                    }
                    else
                    {
                        break;
                    }
                }

                return count;
            }
        }

        private static bool IsLowVariance(Mat templateGray)
        {
            Cv2.MeanStdDev(templateGray, out _, out Scalar stddev);
            return stddev.Val0 < LowVarianceStdDev;
        }

        private void AddNumberedMarker(Rectangle rect)
        {
            if (IsOverlapping(rect))
            {
                return;
            }
            markerCount++;
            EnsureMarkerOverlay();
            markerOverlay.AddMarker(rect, markerCount.ToString());
            markerRects.Add(rect);
        }

        private Rectangle ScaleRectToLogical(Rectangle rect)
        {
            float scale = DpiScale;
            int x = (int)Math.Round(rect.X / scale);
            int y = (int)Math.Round(rect.Y / scale);
            int w = (int)Math.Round(rect.Width / scale);
            int h = (int)Math.Round(rect.Height / scale);
            return new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
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

        private static void ConvertToGray(Mat src, Mat dst)
        {
            if (src.Channels() == 4)
            {
                Cv2.CvtColor(src, dst, ColorConversionCodes.BGRA2GRAY);
            }
            else
            {
                Cv2.CvtColor(src, dst, ColorConversionCodes.BGR2GRAY);
            }
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
            if (markerOverlay != null)
            {
                markerOverlay.Close();
                markerOverlay = null;
            }
            markerCount = 0;
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
        }

        private bool IsCancelHotkey(Keys keyData)
        {
            Keys expected = (Keys)settings.CancelHotkeyCode;
            if ((settings.CancelHotkeyModifiers & 1) != 0) expected |= Keys.Alt;
            if ((settings.CancelHotkeyModifiers & 2) != 0) expected |= Keys.Control;
            if ((settings.CancelHotkeyModifiers & 4) != 0) expected |= Keys.Shift;
            return keyData == expected;
        }

        private void EnsureMarkerOverlay()
        {
            if (markerOverlay == null || markerOverlay.IsDisposed)
            {
                markerOverlay = new MarkerOverlayForm(settings, currentMatchColor);
                markerOverlay.Show();
            }
            else
            {
                markerOverlay.SetBaseColor(currentMatchColor);
            }
        }
    }

    public class MarkerOverlayForm : Form
    {
        private readonly Settings settings;
        private Color baseColor;
        private readonly List<(Rectangle Rect, string Text)> markers = new List<(Rectangle, string)>();

        public MarkerOverlayForm(Settings settings, Color baseColor)
        {
            this.settings = settings;
            this.baseColor = baseColor;
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = Screen.PrimaryScreen.Bounds;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.AutoScaleMode = AutoScaleMode.None;
            this.BackColor = Color.Lime;
            this.TransparencyKey = Color.Lime;
            this.DoubleBuffered = true;
            this.ContextMenuStrip = new ContextMenuStrip();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT for click-through
                return cp;
            }
        }

        public void AddMarker(Rectangle rect, string text)
        {
            markers.Add((rect, text));
            Invalidate();
        }

        public void SetBaseColor(Color color)
        {
            if (baseColor == color)
            {
                return;
            }
            baseColor = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.PageUnit = GraphicsUnit.Pixel;
            using (Pen pen = new Pen(Color.FromArgb(settings.MarkerFillAlpha, baseColor), settings.MarkerBorderThickness))
            using (Font font = new Font("Arial", settings.MarkerFontSize, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(settings.MarkerFillAlpha, baseColor)))  // ← 由 White 改为 baseColor
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                foreach (var marker in markers)
                {
                    Rectangle rect = marker.Rect;
                    g.DrawRectangle(pen, rect);
                    g.DrawString(marker.Text, font, textBrush, rect, format);
                }
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_CONTEXTMENU = 0x007B;
            if (m.Msg == WM_CONTEXTMENU)
            {
                return;
            }
            base.WndProc(ref m);
        }
    }
}
