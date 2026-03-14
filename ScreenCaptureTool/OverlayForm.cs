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
        public static event Action OpenOverlayStateChanged;
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
        private Mat activeScreenHsv;
        private Mat templateColorHist;
        private int templateForegroundPixels = 0;
        private bool hasTemplateColorModel = false;
        private Rectangle captureRegion;
        private const int MinImageScale = 20;
        private const int MaxImageScale = 500;
        private const int MaxMatchCount = 50;
        private const int MinColorForegroundPixels = 25;
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
            this.captureRegion = region;
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
            NotifyOpenOverlayStateChanged();
        }

        public static bool HasOpenOverlays
        {
            get
            {
                lock (OpenSync)
                {
                    return OpenOverlays.Count > 0;
                }
            }
        }

        private static void NotifyOpenOverlayStateChanged()
        {
            OpenOverlayStateChanged?.Invoke();
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
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.W)
            {
                this.Top -= 1;
            }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.S)
            {
                this.Top += 1;
            }
            else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.A)
            {
                this.Left -= 1;
            }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.D)
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

        private static void LogMatch(string algorithm, double score, Rectangle rect, bool accepted, int markerNumber = 0)
        {
            // Logging disabled
        }

        private void StartMatching()
        {
            CloseAllMarkers();
            currentMatchColor = MarkerColorProvider.NextSharedColor();
            bool wasVisible = this.Visible;
            try
            {
                if (wasVisible)
                {
                    this.Hide();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(15);
                }

                // 1. 截取全屏
                System.Drawing.Rectangle bounds = Screen.PrimaryScreen.Bounds;
                using (Bitmap screenBmp = new Bitmap(bounds.Width, bounds.Height))
                {
                    using (Graphics g = Graphics.FromImage(screenBmp))
                    {
                        g.CopyFromScreen(0, 0, 0, 0, bounds.Size);
                    }

                    double threshold = settings.SimilarityThresholdPercent / 100.0;

                    // 仅使用模板匹配主路径（CCoeff + 自适应边缘 + 多尺度）
                    int count = MatchWithTemplate(screenBmp, threshold);

                    if (count == 0)
                    {
                        LogMatch("ALL_FAILED", 0, Rectangle.Empty, false);
                        // 找不到，在当前窗口中心显示红色 X
                        Rectangle rect = new Rectangle(this.Left + this.Width / 2 - 30, this.Top + this.Height / 2 - 30, 60, 60);
                        EnsureMarkerOverlay();
                        markerOverlay.AddMarker(rect, false, "X");
                    }
                }
            }
            catch (Exception ex)
            {
                string logPath = LogException(ex);
                MessageBox.Show("匹配出错: " + ex.Message + "\n日志: " + logPath);
            }
            finally
            {
                if (wasVisible && !this.IsDisposed)
                {
                    this.Show();
                    this.Activate();
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
                PrepareColorValidation(screenMat, templateMat);
                ConvertToGray(screenMat, screenGray);
                ConvertToGray(templateMat, templateGray);
                try
                {
                    return MatchWithTemplate(screenGray, templateGray, threshold);
                }
                finally
                {
                    ClearColorValidation();
                }
            }
        }

        private int MatchWithTemplate(Mat screenGray, Mat templateGray, double threshold)
        {
            int totalCount = 0;
            using (Mat trimmedTemplate = TrimWhiteBorder(templateGray))
            {
                bool trimmed = trimmedTemplate.Width < templateGray.Width || trimmedTemplate.Height < templateGray.Height;
                if (trimmed)
                {
                    totalCount += MatchWithTemplateCore(screenGray, trimmedTemplate, threshold);
                }
            }

            if (ReachedTargetMatchCount() || totalCount >= settings.MaxMatchResults)
            {
                return totalCount;
            }

            if (totalCount < MaxMatchCount)
            {
                totalCount += MatchWithTemplateCore(screenGray, templateGray, threshold);
            }

            return totalCount;
        }

        private int MatchWithTemplateCore(Mat screenGray, Mat templateGray, double threshold)
        {
            if (screenGray.Width < templateGray.Width || screenGray.Height < templateGray.Height)
            {
                return 0;
            }

            // 先用原始尺度（scale=1.0）精确匹配（使用 Adaptive：CCoeff→Edges）
            int totalCount = MatchWithTemplateAdaptive(screenGray, templateGray, threshold);

            if (ReachedTargetMatchCount() || totalCount >= settings.MaxMatchResults)
            {
                return totalCount;
            }

            // 多尺度搜索：以 CCoeff 为主，优先速度
            double relaxed = Math.Max(0.5, threshold - 0.05);
            // 优先尝试接近原图的尺度，命中后立即退出
            double[] scales = new[] {
                0.95, 1.05,
                0.9, 1.1,
                0.85, 1.15,
                0.8, 1.2,
                0.75, 1.25
            };
            for (int i = 0; i < scales.Length; i++)
            {
                if (totalCount >= MaxMatchCount || ReachedTargetMatchCount() || totalCount >= settings.MaxMatchResults) break;

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

                    // 多尺度只跑 CCoeff，避免 Edges 在每个尺度上的额外成本
                    int scaleCount = MatchWithTemplateCCoeff(screenGray, scaledTemplate, relaxed);
                    totalCount += scaleCount;
                }
            }

            return totalCount;
        }

        private static Mat TrimWhiteBorder(Mat templateGray)
        {
            const double WhiteThreshold = 245.0;
            int top = 0;
            int bottom = templateGray.Height - 1;
            int left = 0;
            int right = templateGray.Width - 1;

            while (top <= bottom)
            {
                Cv2.MinMaxLoc(templateGray.Row(top), out double minVal, out _, out _, out _);
                if (minVal < WhiteThreshold) break;
                top++;
            }

            while (bottom >= top)
            {
                Cv2.MinMaxLoc(templateGray.Row(bottom), out double minVal, out _, out _, out _);
                if (minVal < WhiteThreshold) break;
                bottom--;
            }

            while (left <= right)
            {
                Cv2.MinMaxLoc(templateGray.Col(left), out double minVal, out _, out _, out _);
                if (minVal < WhiteThreshold) break;
                left++;
            }

            while (right >= left)
            {
                Cv2.MinMaxLoc(templateGray.Col(right), out double minVal, out _, out _, out _);
                if (minVal < WhiteThreshold) break;
                right--;
            }

            int width = right - left + 1;
            int height = bottom - top + 1;
            if (width < 8 || height < 8)
            {
                return templateGray.Clone();
            }

            Rect roi = new Rect(left, top, width, height);
            return new Mat(templateGray, roi).Clone();
        }

        private int MatchWithTemplateAdaptive(Mat screenGray, Mat templateGray, double threshold)
        {
            int totalCount = 0;
            bool lowVariance = IsLowVariance(templateGray);
            if (!lowVariance)
            {
                totalCount += MatchWithTemplateCCoeff(screenGray, templateGray, threshold);
            }

            if (totalCount == 0)
            {
                totalCount += MatchWithTemplateByEdges(screenGray, templateGray, threshold);
            }

            return totalCount;
        }

        private int MatchWithTemplateByEdges(Mat screenGray, Mat templateGray, double threshold)
        {
            if (ReachedTargetMatchCount())
            {
                return 0;
            }

            using (Mat screenEdges = new Mat())
            using (Mat templateEdges = new Mat())
            {
                Cv2.Canny(screenGray, screenEdges, 40, 120);
                Cv2.Canny(templateGray, templateEdges, 40, 120);

                int edgePixels = Cv2.CountNonZero(templateEdges);
                if (edgePixels < 20)
                {
                    return 0;
                }

                double edgeThreshold = Math.Max(0.4, threshold - 0.12);
                return MatchWithTemplateCCoeff(screenEdges, templateEdges, edgeThreshold);
            }
        }

        private int MatchWithTemplateCCoeff(Mat screenGray, Mat templateGray, double threshold)
        {
            if (ReachedTargetMatchCount())
            {
                return 0;
            }

            using (Mat result = new Mat())
            {
                Cv2.MatchTemplate(screenGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double peakVal, out _, out OpenCvSharp.Point peakLoc);
                LogMatch("Template_CCoeff_Peak", peakVal, new Rectangle(peakLoc.X, peakLoc.Y, templateGray.Width, templateGray.Height), false);
                Cv2.Threshold(result, result, threshold, 1.0, ThresholdTypes.Tozero);

                int count = 0;
                while (count < MaxMatchCount && !ReachedTargetMatchCount())
                {
                    double minVal, maxVal;
                    OpenCvSharp.Point minLoc, maxLoc;
                    Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);
                    if (double.IsNaN(maxVal))
                    {
                        return count;
                    }

                    if (maxVal >= threshold)
                    {
                        Rectangle rect = new Rectangle(maxLoc.X, maxLoc.Y, templateGray.Width, templateGray.Height);
                        bool colorOk = PassesColorValidation(rect);
                        if (colorOk && !IsCaptureRegion(rect) && !IsOverlapping(rect))
                        {
                            int markerNo = AddNumberedMarker(rect);
                            if (markerNo > 0)
                            {
                                count++;
                                LogMatch("Template_CCoeff", maxVal, rect, true, markerNo);
                            }
                        }
                        // 清除整个模板大小的矩形区域，防止 1px 偏移重复匹配
                        SuppressResultRegion(result, maxLoc.X, maxLoc.Y, templateGray.Width, templateGray.Height, 0);
                    }
                    else
                    {
                        break;
                    }
                }

                return count;
            }
        }

        private static void SuppressResultRegion(Mat result, int x, int y, int templateW, int templateH, double fillValue)
        {
            int x0 = Math.Max(0, x - templateW / 2);
            int y0 = Math.Max(0, y - templateH / 2);
            int x1 = Math.Min(result.Width, x + templateW);
            int y1 = Math.Min(result.Height, y + templateH);
            if (x1 > x0 && y1 > y0)
            {
                using (Mat roi = new Mat(result, new OpenCvSharp.Rect(x0, y0, x1 - x0, y1 - y0)))
                {
                    roi.SetTo(new Scalar(fillValue));
                }
            }
        }

        private static bool IsLowVariance(Mat templateGray)
        {
            Cv2.MeanStdDev(templateGray, out _, out Scalar stddev);
            return stddev.Val0 < LowVarianceStdDev;
        }

        private void PrepareColorValidation(Mat screenMat, Mat templateMat)
        {
            ClearColorValidation();

            using (Mat screenBgr = new Mat())
            using (Mat templateBgr = new Mat())
            using (Mat templateHsv = new Mat())
            using (Mat templateMask = new Mat())
            {
                if (screenMat.Channels() == 4)
                {
                    Cv2.CvtColor(screenMat, screenBgr, ColorConversionCodes.BGRA2BGR);
                }
                else
                {
                    screenMat.CopyTo(screenBgr);
                }

                if (templateMat.Channels() == 4)
                {
                    Cv2.CvtColor(templateMat, templateBgr, ColorConversionCodes.BGRA2BGR);
                }
                else
                {
                    templateMat.CopyTo(templateBgr);
                }

                activeScreenHsv = new Mat();
                Cv2.CvtColor(screenBgr, activeScreenHsv, ColorConversionCodes.BGR2HSV);

                Cv2.CvtColor(templateBgr, templateHsv, ColorConversionCodes.BGR2HSV);
                BuildForegroundMask(templateHsv, templateMask);
                templateForegroundPixels = Cv2.CountNonZero(templateMask);
                if (templateForegroundPixels < MinColorForegroundPixels)
                {
                    return;
                }

                int[] channels = { 0, 1 };
                int[] histSize = { 30, 32 };
                Rangef[] ranges = { new Rangef(0, 180), new Rangef(0, 256) };
                templateColorHist = new Mat();
                Cv2.CalcHist(new[] { templateHsv }, channels, templateMask, templateColorHist, 2, histSize, ranges);
                Cv2.Normalize(templateColorHist, templateColorHist, 0, 1, NormTypes.MinMax);
                hasTemplateColorModel = true;
            }
        }

        private void ClearColorValidation()
        {
            if (activeScreenHsv != null)
            {
                activeScreenHsv.Dispose();
                activeScreenHsv = null;
            }

            if (templateColorHist != null)
            {
                templateColorHist.Dispose();
                templateColorHist = null;
            }

            templateForegroundPixels = 0;
            hasTemplateColorModel = false;
        }

        private bool PassesColorValidation(Rectangle rect)
        {
            if (activeScreenHsv == null || activeScreenHsv.Empty() || !hasTemplateColorModel || templateColorHist == null)
            {
                return true;
            }

            if (rect.X < 0 || rect.Y < 0 ||
                rect.X + rect.Width > activeScreenHsv.Width ||
                rect.Y + rect.Height > activeScreenHsv.Height)
            {
                return false;
            }

            using (Mat roi = new Mat(activeScreenHsv, new OpenCvSharp.Rect(rect.X, rect.Y, rect.Width, rect.Height)))
            using (Mat roiMask = new Mat())
            using (Mat roiHist = new Mat())
            {
                BuildForegroundMask(roi, roiMask);
                int roiForegroundPixels = Cv2.CountNonZero(roiMask);
                int minRequired = Math.Max(MinColorForegroundPixels, (int)Math.Round(templateForegroundPixels * 0.25));
                if (roiForegroundPixels < minRequired)
                {
                    return false;
                }

                int[] channels = { 0, 1 };
                int[] histSize = { 30, 32 };
                Rangef[] ranges = { new Rangef(0, 180), new Rangef(0, 256) };
                Cv2.CalcHist(new[] { roi }, channels, roiMask, roiHist, 2, histSize, ranges);
                Cv2.Normalize(roiHist, roiHist, 0, 1, NormTypes.MinMax);

                double similarity = Cv2.CompareHist(templateColorHist, roiHist, HistCompMethods.Correl);
                if (double.IsNaN(similarity))
                {
                    return false;
                }

                double colorThreshold = Math.Max(0.0, Math.Min(1.0, settings.ColorSimilarityThreshold));
                return similarity >= colorThreshold;
            }
        }

        private static void BuildForegroundMask(Mat hsv, Mat mask)
        {
            using (Mat whiteMask = new Mat())
            {
                Cv2.InRange(hsv, new Scalar(0, 0, 230), new Scalar(180, 40, 255), whiteMask);
                Cv2.BitwiseNot(whiteMask, mask);
            }
        }

        private bool ReachedTargetMatchCount()
        {
            return markerCount >= settings.MaxMatchResults;
        }

        private int AddNumberedMarker(Rectangle rect)
        {
            if (IsOverlapping(rect))
            {
                return 0;
            }
            markerCount++;
            EnsureMarkerOverlay();
            markerOverlay.AddMarker(rect, true, null);
            markerRects.Add(rect);
            return markerCount;
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

        /// <summary>
        /// 检查候选区域是否位于抓图位置（即模板自身所在位置），如果是则跳过。
        /// </summary>
        private bool IsCaptureRegion(Rectangle rect)
        {
            if (captureRegion.IsEmpty || captureRegion.Width <= 0 || captureRegion.Height <= 0)
            {
                return false;
            }

            Rectangle inter = Rectangle.Intersect(captureRegion, rect);
            if (inter.Width <= 0 || inter.Height <= 0)
            {
                return false;
            }

            double interArea = inter.Width * inter.Height;
            double rectArea = rect.Width * rect.Height;
            return rectArea > 0 && (interArea / rectArea) >= 0.5;
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
            bool removed = false;
            if (disposing)
            {
                CloseAllMarkers();
                ClearColorValidation();
                image?.Dispose();
            }
            lock (OpenSync)
            {
                removed = OpenOverlays.Remove(this);
            }
            if (removed)
            {
                NotifyOpenOverlayStateChanged();
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
                markerOverlay = new MarkerOverlayForm(settings, currentMatchColor, image);
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
        private readonly struct MarkerItem
        {
            public MarkerItem(Rectangle rect, bool showPreview, string text)
            {
                Rect = rect;
                ShowPreview = showPreview;
                Text = text;
            }

            public Rectangle Rect { get; }
            public bool ShowPreview { get; }
            public string Text { get; }
        }

        private readonly Settings settings;
        private readonly Bitmap previewSource;
        private readonly Rectangle previewSourceRect;
        private Color baseColor;
        private readonly List<MarkerItem> markers = new List<MarkerItem>();

        public MarkerOverlayForm(Settings settings, Color baseColor, Bitmap previewSource)
        {
            this.settings = settings;
            this.baseColor = baseColor;
            this.previewSource = previewSource;
            previewSourceRect = BuildPreviewSourceRect(previewSource);
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

        public void AddMarker(Rectangle rect, bool showPreview, string text)
        {
            markers.Add(new MarkerItem(rect, showPreview, text));
            Invalidate();
        }

        private static Rectangle BuildPreviewSourceRect(Bitmap source)
        {
            int startX = Math.Min(source.Width - 1, Math.Max(0, source.Width / 2));
            int width = Math.Max(1, source.Width - startX);
            return new Rectangle(startX, 0, width, Math.Max(1, source.Height));
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
            using (ImageAttributes imageAttributes = new ImageAttributes())
            using (Pen pen = new Pen(Color.FromArgb(settings.MarkerFillAlpha, baseColor), settings.MarkerBorderThickness))
            using (Font font = new Font("Arial", settings.MarkerFontSize, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(settings.MarkerFillAlpha, baseColor)))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                ColorMatrix colorMatrix = new ColorMatrix
                {
                    Matrix33 = Math.Max(0.1f, Math.Min(1.0f, settings.DefaultOpacity / 100f))
                };
                imageAttributes.SetColorMatrix(colorMatrix);

                foreach (var marker in markers)
                {
                    Rectangle rect = marker.Rect;
                    if (marker.ShowPreview)
                    {
                        int rightHalfX = rect.X + (rect.Width / 2);
                        Rectangle previewRect = new Rectangle(rightHalfX, rect.Y, Math.Max(1, rect.Right - rightHalfX), rect.Height);
                        g.DrawImage(
                            previewSource,
                            previewRect,
                            previewSourceRect.X,
                            previewSourceRect.Y,
                            previewSourceRect.Width,
                            previewSourceRect.Height,
                            GraphicsUnit.Pixel,
                            imageAttributes);
                    }

                    g.DrawRectangle(pen, rect);
                    if (!string.IsNullOrEmpty(marker.Text))
                    {
                        g.DrawString(marker.Text, font, textBrush, rect, format);
                    }
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
