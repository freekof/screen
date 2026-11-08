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
        private readonly bool stampMode;
        private readonly List<MatchResultForm> resultMarkerForms = new List<MatchResultForm>();
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

        public OverlayForm(Bitmap img, System.Drawing.Rectangle region, Settings settings, bool stampMode = false)
        {
            this.image = img;
            this.settings = settings;
            this.captureRegion = region;
            this.stampMode = stampMode;
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
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(image, destRect);
                }
                else
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
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
            using (Mat trimmedTemplate = TrimWhiteBorder(templateGray, out Rectangle trimRect))
            {
                bool wasTrimmed = trimRect.X != 0 || trimRect.Y != 0 ||
                                  trimRect.Width != templateGray.Width || trimRect.Height != templateGray.Height;
                if (wasTrimmed)
                {
                    int count = MatchWithTemplateCore(screenGray, trimmedTemplate, threshold, trimRect);
                    if (ReachedTargetMatchCount() || count >= settings.MaxMatchResults)
                    {
                        return count;
                    }

                    // Trimmed match had too few results: try one exact-scale pass with the full template.
                    return count + MatchWithTemplateAdaptive(screenGray, templateGray, threshold,
                        new Rectangle(0, 0, templateGray.Width, templateGray.Height), 1.0);
                }

                return MatchWithTemplateCore(screenGray, templateGray, threshold,
                    new Rectangle(0, 0, templateGray.Width, templateGray.Height));
            }
        }

        private Rectangle MapMatchToSourceImage(Rectangle matchRect, Rectangle sourceCrop, double scale)
        {
            scale = Math.Max(0.01, scale);
            int x = matchRect.X - (int)Math.Round(sourceCrop.X * scale);
            int y = matchRect.Y - (int)Math.Round(sourceCrop.Y * scale);
            int width = Math.Max(1, (int)Math.Round(image.Width * scale));
            int height = Math.Max(1, (int)Math.Round(image.Height * scale));
            return new Rectangle(x, y, width, height);
        }

        private int MatchWithTemplateCore(Mat screenGray, Mat templateGray, double threshold, Rectangle sourceCrop)
        {
            if (screenGray.Width < templateGray.Width || screenGray.Height < templateGray.Height)
            {
                return 0;
            }

            int totalCount = MatchWithTemplateAdaptive(screenGray, templateGray, threshold, sourceCrop, 1.0);

            if (ReachedTargetMatchCount() || totalCount >= settings.MaxMatchResults)
            {
                return totalCount;
            }

            double relaxed = Math.Max(0.5, threshold - 0.05);
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
                    int scaleCount = MatchWithTemplateCCoeff(screenGray, scaledTemplate, relaxed, sourceCrop, scales[i]);
                    totalCount += scaleCount;
                }
            }

            return totalCount;
        }

        private static Mat TrimWhiteBorder(Mat templateGray, out Rectangle cropRect)
        {
            const double WhiteThreshold = 245.0;
            int top = 0;
            int bottom = templateGray.Height - 1;
            int left = 0;
            int right = templateGray.Width - 1;

            while (top <= bottom)
            {
                using Mat row = templateGray.Row(top);
                Cv2.MinMaxLoc(row, out double minVal, out _, out _, out _);
                if (minVal < WhiteThreshold) break;
                top++;
            }

            while (bottom >= top)
            {
                using Mat row = templateGray.Row(bottom);
                Cv2.MinMaxLoc(row, out double minVal, out _, out _, out _);
                if (minVal < WhiteThreshold) break;
                bottom--;
            }

            while (left <= right)
            {
                using Mat col = templateGray.Col(left);
                Cv2.MinMaxLoc(col, out double minVal, out _, out _, out _);
                if (minVal < WhiteThreshold) break;
                left++;
            }

            while (right >= left)
            {
                using Mat col = templateGray.Col(right);
                Cv2.MinMaxLoc(col, out double minVal, out _, out _, out _);
                if (minVal < WhiteThreshold) break;
                right--;
            }

            int width = right - left + 1;
            int height = bottom - top + 1;
            if (width < 8 || height < 8)
            {
                cropRect = new Rectangle(0, 0, templateGray.Width, templateGray.Height);
                return templateGray.Clone();
            }

            Rect roi = new Rect(left, top, width, height);
            cropRect = new Rectangle(left, top, width, height);
            using Mat cropped = new Mat(templateGray, roi);
            return cropped.Clone();
        }

        private int MatchWithTemplateAdaptive(Mat screenGray, Mat templateGray, double threshold,
            Rectangle sourceCrop, double scale)
        {
            int totalCount = 0;
            bool lowVariance = IsLowVariance(templateGray);
            if (!lowVariance)
            {
                totalCount += MatchWithTemplateCCoeff(screenGray, templateGray, threshold, sourceCrop, scale);
            }

            if (totalCount == 0)
            {
                totalCount += MatchWithTemplateByEdges(screenGray, templateGray, threshold, sourceCrop, scale);
            }

            return totalCount;
        }

        private int MatchWithTemplateByEdges(Mat screenGray, Mat templateGray, double threshold,
            Rectangle sourceCrop, double scale)
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
                return MatchWithTemplateCCoeff(screenEdges, templateEdges, edgeThreshold, sourceCrop, scale);
            }
        }

        private int MatchWithTemplateCCoeff(Mat screenGray, Mat templateGray, double threshold,
            Rectangle sourceCrop, double scale)
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
                        Rectangle matchRect = new Rectangle(maxLoc.X, maxLoc.Y, templateGray.Width, templateGray.Height);
                        Rectangle imageRect = MapMatchToSourceImage(matchRect, sourceCrop, scale);
                        if (!IsCaptureRegion(imageRect) && !IsOverlapping(imageRect) && PassesColorValidation(imageRect))
                        {
                            int markerNo = AddNumberedMarker(imageRect);
                            if (markerNo > 0)
                            {
                                count++;
                                LogMatch("Template_CCoeff", maxVal, imageRect, true, markerNo);
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
            int x0 = Math.Max(0, x - templateW + 1);
            int y0 = Math.Max(0, y - templateH + 1);
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
            if (!stampMode)
            {
                // F1 启动模式：完整显示整张原图，生成独立可拖动、可缩放的标记浮窗
                var resultForm = new MatchResultForm(image, rect, settings, currentMatchColor);
                resultMarkerForms.Add(resultForm);
                resultForm.Show();
            }
            else
            {
                // F2 印章模式：保持原来标记框减半，只显示右半边图像，对齐方式按 7956ae8，鼠标穿透
                EnsureMarkerOverlay();
                markerOverlay.AddMarker(rect, true, null);
            }
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
            if (resultMarkerForms.Count > 0)
            {
                foreach (var f in resultMarkerForms)
                {
                    if (!f.IsDisposed)
                    {
                        f.Close();
                    }
                }
                resultMarkerForms.Clear();
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

        public static void ApplySettingsToOpenOverlays(Settings s)
        {
            List<OverlayForm> overlays;
            lock (OpenSync)
            {
                overlays = new List<OverlayForm>(OpenOverlays);
            }
            foreach (var o in overlays)
            {
                o.ApplySettings(s);
            }
        }

        public void ApplySettings(Settings s)
        {
            this.settings = s;
            this.Opacity = s.DefaultOpacity / 100.0;
            this.Size = FormSize;
            this.Invalidate();

            if (markerOverlay != null && !markerOverlay.IsDisposed)
            {
                markerOverlay.UpdateSettings(s);
            }

            foreach (var f in resultMarkerForms)
            {
                if (!f.IsDisposed)
                {
                    f.UpdateSettings(s);
                }
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

        private Settings settings;
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
            this.Opacity = Math.Clamp(settings.MarkerFillAlpha / 200.0, 0.10, 1.00);
            this.ContextMenuStrip = new ContextMenuStrip();
        }

        public void UpdateSettings(Settings s)
        {
            this.settings = s;
            this.Opacity = Math.Clamp(s.MarkerFillAlpha / 200.0, 0.10, 1.00);
            Invalidate();
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
            int startX = Math.Max(0, source.Width / 2);
            return new Rectangle(startX, 0, Math.Max(1, source.Width - startX), Math.Max(1, source.Height));
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
            using (Pen pen = new Pen(baseColor, settings.MarkerBorderThickness))
            using (Font font = new Font("Arial", 28, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(baseColor))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                foreach (var marker in markers)
                {
                    Rectangle rect = marker.Rect;
                    if (marker.ShowPreview)
                    {
                        double scaleX = rect.Width / (double)Math.Max(1, previewSource.Width);
                        double scaleY = rect.Height / (double)Math.Max(1, previewSource.Height);
                        Rectangle previewRect = new Rectangle(
                            rect.X + (int)Math.Round(previewSourceRect.X * scaleX),
                            rect.Y + (int)Math.Round(previewSourceRect.Y * scaleY),
                            Math.Max(1, (int)Math.Round(previewSourceRect.Width * scaleX)),
                            Math.Max(1, (int)Math.Round(previewSourceRect.Height * scaleY)));

                        if (Math.Abs(scaleX - 1.0) < 0.01 && Math.Abs(scaleY - 1.0) < 0.01)
                        {
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                        }
                        else
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                        }
                        g.DrawImage(
                            previewSource,
                            previewRect,
                            previewSourceRect.X,
                            previewSourceRect.Y,
                            previewSourceRect.Width,
                            previewSourceRect.Height,
                            GraphicsUnit.Pixel);
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

    /// <summary>
    /// F1 启动模式下生成的标记框窗口：完整显示整张原图，支持鼠标拖动、滚轮缩放、右键关闭，整体透明度受 MarkerFillAlpha 控制。
    /// </summary>
    public class MatchResultForm : Form
    {
        private readonly Bitmap image;
        private Settings settings;
        private readonly Color matchColor;
        private System.Drawing.Point lastMousePos;
        private bool isDragging = false;
        private bool isResizing = false;
        private const int MinImageScale = 20;
        private const int MaxImageScale = 500;
        private int imageScale = 100;

        public int ImageScale
        {
            get => imageScale;
            set
            {
                int newScale = Math.Clamp(value, MinImageScale, MaxImageScale);
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
                int border = settings.MarkerBorderThickness * 2;
                System.Drawing.Size size = ImageSize;
                return new System.Drawing.Size(size.Width + border, size.Height + border);
            }
        }

        public MatchResultForm(Bitmap img, Rectangle initialRect, Settings settings, Color color)
        {
            this.image = img;
            this.settings = settings;
            this.matchColor = color;

            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ControlBox = false;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.AutoScaleMode = AutoScaleMode.None;
            this.WindowState = FormWindowState.Normal;
            this.DoubleBuffered = true;
            this.Opacity = Math.Clamp(settings.MarkerFillAlpha / 200.0, 0.10, 1.00);

            int calcScale = (int)Math.Round((initialRect.Width / (double)Math.Max(1, image.Width)) * 100.0);
            this.imageScale = Math.Clamp(calcScale, MinImageScale, MaxImageScale);

            this.Size = FormSize;
            int border = settings.MarkerBorderThickness;
            this.Location = new Point(initialRect.X - border, initialRect.Y - border);

            this.MouseDown += MatchResultForm_MouseDown;
            this.MouseMove += MatchResultForm_MouseMove;
            this.MouseUp += MatchResultForm_MouseUp;
            this.MouseWheel += MatchResultForm_MouseWheel;
            this.KeyDown += MatchResultForm_KeyDown;
        }

        public void UpdateSettings(Settings s)
        {
            this.settings = s;
            this.Opacity = Math.Clamp(s.MarkerFillAlpha / 200.0, 0.10, 1.00);
            AutoSizeForm();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.Style &= ~0x00080000;  // WS_SYSMENU
                cp.Style &= ~0x00020000;  // WS_MINIMIZEBOX
                cp.Style &= ~0x00010000;  // WS_MAXIMIZEBOX
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
            int border = settings.MarkerBorderThickness;
            System.Drawing.Size drawSize = ImageSize;
            if (drawSize.Width > 0 && drawSize.Height > 0)
            {
                Rectangle destRect = new Rectangle(border, border, drawSize.Width, drawSize.Height);
                if (ImageScale == 100)
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(image, destRect);
                }
                else
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(image, destRect);
                }

                using (Pen pen = new Pen(matchColor, border))
                {
                    g.DrawRectangle(pen, destRect);
                }

                // 右下角缩放手柄提示
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(150, matchColor)))
                {
                    Point[] pts = {
                        new Point(destRect.Right, destRect.Bottom - 10),
                        new Point(destRect.Right, destRect.Bottom),
                        new Point(destRect.Right - 10, destRect.Bottom)
                    };
                    g.FillPolygon(brush, pts);
                }
            }
        }

        private void MatchResultForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (e.X > this.Width - 20 && e.Y > this.Height - 20)
                {
                    isResizing = true;
                }
                else
                {
                    isDragging = true;
                    lastMousePos = e.Location;
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                this.Close();
            }
        }

        private void MatchResultForm_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.X > this.Width - 20 && e.Y > this.Height - 20)
                this.Cursor = Cursors.SizeNWSE;
            else
                this.Cursor = Cursors.SizeAll;

            if (isResizing)
            {
                int border = settings.MarkerBorderThickness * 2;
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

        private void MatchResultForm_MouseUp(object sender, MouseEventArgs e)
        {
            isDragging = false;
            isResizing = false;
        }

        private void MatchResultForm_MouseWheel(object sender, MouseEventArgs e)
        {
            int delta = e.Delta > 0 ? 10 : -10;
            ImageScale += delta;
        }

        private void MatchResultForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || IsCancelHotkey(e.KeyData))
            {
                this.Close();
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
    }
}
