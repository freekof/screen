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

        private static readonly string MatchLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "match_algorithm.log");

        private static void LogMatch(string algorithm, double score, Rectangle rect, bool accepted)
        {
            try
            {
                string line = string.Format("{0} | Algorithm={1} | Score={2:F4} | Rect=({3},{4},{5},{6}) | Accepted={7}",
                    DateTime.Now.ToString("u"), algorithm, score, rect.X, rect.Y, rect.Width, rect.Height, accepted);
                File.AppendAllText(MatchLogPath, line + Environment.NewLine);
            }
            catch
            {
                // Ignore logging failures.
            }
        }

        private static void LogMatchHeader(string templateInfo)
        {
            try
            {
                string line = string.Format("{0} | === New Match === | Template={1}",
                    DateTime.Now.ToString("u"), templateInfo);
                File.AppendAllText(MatchLogPath, line + Environment.NewLine);
            }
            catch { }
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

                    LogMatchHeader(string.Format("{0}x{1}", image.Width, image.Height));
                    double threshold = settings.SimilarityThresholdPercent / 100.0;

                    // 依次尝试所有算法，累积匹配结果
                    int count = 0;

                    // 1) ORB 特征匹配
                    count += TryMatchWithOrb(screenBmp);

                    // 2) 灰度模板匹配（含多尺度+旋转）— 最重要的算法，始终运行
                    if (count < MaxMatchCount)
                        count += MatchWithTemplate(screenBmp, threshold);

                    // 3) HSV 色彩直方图滑窗 — 仅在前面算法未找到足够结果时尝试
                    if (count < 2)
                        count += TryMatchWithHistogram(screenBmp, threshold);

                    // 4) pHash 多尺度滑窗
                    if (count < 2)
                        count += TryMatchWithPHash(screenBmp, threshold);

                    // 5) Hu 矩形状匹配（轮廓）
                    if (count < 2)
                        count += TryMatchWithHuMoments(screenBmp, threshold);

                    if (count == 0)
                    {
                        LogMatch("ALL_FAILED", 0, Rectangle.Empty, false);
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
            finally
            {
                if (wasVisible && !this.IsDisposed)
                {
                    this.Show();
                    this.Activate();
                }
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

                            LogMatch("ORB_GoodMatches", good.Count, Rectangle.Empty, false);

                            if (good.Count < 8)
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
                                LogMatch("ORB_Homography", scale, singleRect, true);
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
            int totalCount = 0;
            using (Mat trimmedTemplate = TrimWhiteBorder(templateGray))
            {
                bool trimmed = trimmedTemplate.Width < templateGray.Width || trimmedTemplate.Height < templateGray.Height;
                if (trimmed)
                {
                    totalCount += MatchWithTemplateCore(screenGray, trimmedTemplate, threshold);
                }
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

            // 先用原始尺度（scale=1.0）精确匹配（使用完整 Adaptive：CCoeff→SqDiff→Edges）
            int totalCount = MatchWithTemplateAdaptive(screenGray, templateGray, threshold);

            // 多尺度搜索：只用 CCoeff（SqDiff 在不同尺度下误报率极高）
            double relaxed = Math.Max(0.5, threshold - 0.05);
            // 加细步长，覆盖 0.5~1.5 范围
            double[] scales = new[] {
                0.95, 1.05,
                0.9, 1.1,
                0.85, 1.15,
                0.8, 1.2,
                0.75, 1.25,
                0.7, 1.3,
                0.65, 1.35,
                0.6, 1.4,
                0.55, 1.45,
                0.5, 1.5
            };
            double[] angles = new[] { -30.0, -24.0, -18.0, -12.0, -6.0, 6.0, 12.0, 18.0, 24.0, 30.0 };
            for (int i = 0; i < scales.Length; i++)
            {
                if (totalCount >= MaxMatchCount) break;

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

                    // 多尺度下用 CCoeff + SqDiff（SqDiff 有二次验证，不会误报）
                    int scaleCount = MatchWithTemplateAdaptive(screenGray, scaledTemplate, relaxed);
                    totalCount += scaleCount;

                    if (totalCount >= MaxMatchCount) break;

                    int rotCount = MatchWithTemplateRotations(screenGray, scaledTemplate, relaxed, angles);
                    totalCount += rotCount;
                }
            }

            return totalCount;
        }

        private int MatchWithTemplateRotations(Mat screenGray, Mat templateGray, double threshold, double[] angles)
        {
            int totalCount = 0;
            for (int i = 0; i < angles.Length; i++)
            {
                if (totalCount >= MaxMatchCount) break;

                using (Mat rotatedTemplate = new Mat())
                using (Mat rotatedMask = new Mat())
                {
                    RotateTemplateWithMask(templateGray, angles[i], rotatedTemplate, rotatedMask);

                    if (rotatedTemplate.Width < 8 || rotatedTemplate.Height < 8)
                    {
                        continue;
                    }
                    if (rotatedTemplate.Width > screenGray.Width || rotatedTemplate.Height > screenGray.Height)
                    {
                        continue;
                    }

                    int count = MatchWithTemplateMasked(screenGray, rotatedTemplate, rotatedMask, threshold);
                    totalCount += count;
                }
            }

            return totalCount;
        }

        private static void RotateTemplateWithMask(Mat src, double angle, Mat dst, Mat mask)
        {
            Point2f center = new Point2f(src.Width / 2f, src.Height / 2f);
            using (Mat rot = Cv2.GetRotationMatrix2D(center, angle, 1.0))
            {
                double radians = angle * Math.PI / 180.0;
                double absCos = Math.Abs(Math.Cos(radians));
                double absSin = Math.Abs(Math.Sin(radians));
                int boundW = Math.Max(1, (int)Math.Ceiling(src.Height * absSin + src.Width * absCos));
                int boundH = Math.Max(1, (int)Math.Ceiling(src.Height * absCos + src.Width * absSin));

                double tx = rot.At<double>(0, 2) + (boundW / 2.0 - center.X);
                double ty = rot.At<double>(1, 2) + (boundH / 2.0 - center.Y);
                rot.Set(0, 2, tx);
                rot.Set(1, 2, ty);

                // 旋转模板图像，填充区域用 0（黑色）
                Cv2.WarpAffine(src, dst, rot, new OpenCvSharp.Size(boundW, boundH),
                    InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(0));

                // 生成 mask：原始区域全白，旋转后填充区域为黑
                using (Mat ones = Mat.Ones(src.Height, src.Width, MatType.CV_8UC1) * 255)
                {
                    Cv2.WarpAffine(ones, mask, rot, new OpenCvSharp.Size(boundW, boundH),
                        InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(0));
                    // 二值化确保 mask 干净（插值可能产生中间值）
                    Cv2.Threshold(mask, mask, 128, 255, ThresholdTypes.Binary);
                }
            }
        }

        /// <summary>
        /// 带 mask 的模板匹配：只比较 mask 中有效（非零）的像素区域。
        /// 用于旋转后的模板匹配，排除旋转产生的填充边界。
        /// </summary>
        private int MatchWithTemplateMasked(Mat screenGray, Mat templateGray, Mat mask, double threshold)
        {
            if (screenGray.Width < templateGray.Width || screenGray.Height < templateGray.Height)
            {
                return 0;
            }

            using (Mat result = new Mat())
            {
                // CCoeffNormed 支持 mask 参数
                Cv2.MatchTemplate(screenGray, templateGray, result, TemplateMatchModes.CCoeffNormed, mask);
                Cv2.MinMaxLoc(result, out _, out double peakVal, out _, out OpenCvSharp.Point peakLoc);
                LogMatch("Template_Rotated_Masked_Peak", peakVal,
                    new Rectangle(peakLoc.X, peakLoc.Y, templateGray.Width, templateGray.Height), false);

                Cv2.Threshold(result, result, threshold, 1.0, ThresholdTypes.Tozero);

                int count = 0;
                while (count < MaxMatchCount)
                {
                    double minVal, maxVal;
                    OpenCvSharp.Point minLoc, maxLoc;
                    Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);
                    if (double.IsNaN(maxVal) || maxVal < threshold)
                    {
                        break;
                    }

                    Rectangle rect = new Rectangle(maxLoc.X, maxLoc.Y, templateGray.Width, templateGray.Height);
                    LogMatch("Template_Rotated_Masked", maxVal, rect, !IsOverlapping(rect));
                    if (!IsOverlapping(rect))
                    {
                        count++;
                        AddNumberedMarker(rect);
                    }
                    SuppressResultRegion(result, maxLoc.X, maxLoc.Y, templateGray.Width, templateGray.Height, 0);
                }

                return count;
            }
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

            // SqDiff 现在有二次验证，可以安全使用，但仅在 CCoeff 无结果时
            if (totalCount == 0)
            {
                totalCount += MatchWithTemplateSqDiff(screenGray, templateGray, threshold);
            }

            if (totalCount == 0)
            {
                totalCount += MatchWithTemplateByEdges(screenGray, templateGray, threshold);
            }

            return totalCount;
        }

        private int MatchWithTemplateByEdges(Mat screenGray, Mat templateGray, double threshold)
        {
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
            using (Mat result = new Mat())
            {
                Cv2.MatchTemplate(screenGray, templateGray, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double peakVal, out _, out OpenCvSharp.Point peakLoc);
                LogMatch("Template_CCoeff_Peak", peakVal, new Rectangle(peakLoc.X, peakLoc.Y, templateGray.Width, templateGray.Height), false);
                Cv2.Threshold(result, result, threshold, 1.0, ThresholdTypes.Tozero);

                int count = 0;
                while (true)
                {
                    double minVal, maxVal;
                    OpenCvSharp.Point minLoc, maxLoc;
                    Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);
                    if (double.IsNaN(maxVal))
                    {
                        return count;
                    }

                    if (maxVal >= threshold && count < MaxMatchCount)
                    {
                        Rectangle rect = new Rectangle(maxLoc.X, maxLoc.Y, templateGray.Width, templateGray.Height);
                        LogMatch("Template_CCoeff", maxVal, rect, !IsOverlapping(rect));
                        if (!IsOverlapping(rect))
                        {
                            count++;
                            AddNumberedMarker(rect);
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

        private int MatchWithTemplateSqDiff(Mat screenGray, Mat templateGray, double threshold)
        {
            double diffThreshold = Math.Max(0.0, 1.0 - threshold);

            // 预计算模板的边缘特征，用于二次验证
            using (Mat templateEdges = new Mat())
            {
                Cv2.Canny(templateGray, templateEdges, 40, 120);
                int templateEdgeCount = Cv2.CountNonZero(templateEdges);

                using (Mat result = new Mat())
                {
                    Cv2.MatchTemplate(screenGray, templateGray, result, TemplateMatchModes.SqDiffNormed);
                    Cv2.MinMaxLoc(result, out double sqPeakMin, out _, out OpenCvSharp.Point sqPeakLoc, out _);
                    LogMatch("Template_SqDiff_Peak", 1.0 - sqPeakMin, new Rectangle(sqPeakLoc.X, sqPeakLoc.Y, templateGray.Width, templateGray.Height), false);

                    int count = 0;
                    while (true)
                    {
                        double minVal, maxVal;
                        OpenCvSharp.Point minLoc, maxLoc;
                        Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);

                        if (minVal <= diffThreshold && count < MaxMatchCount)
                        {
                            Rectangle rect = new Rectangle(minLoc.X, minLoc.Y, templateGray.Width, templateGray.Height);
                            double score = 1.0 - minVal;

                            // 二次验证：检查候选区域的边缘结构是否与模板相似
                            bool verified = VerifyCandidate(screenGray, templateGray, templateEdges, templateEdgeCount, rect);
                            LogMatch("Template_SqDiff", score, rect, verified && !IsOverlapping(rect));

                            if (verified && !IsOverlapping(rect))
                            {
                                count++;
                                AddNumberedMarker(rect);
                            }
                            SuppressResultRegion(result, minLoc.X, minLoc.Y, templateGray.Width, templateGray.Height, 1.0);
                        }
                        else
                        {
                            break;
                        }
                    }

                    return count;
                }
            }
        }

        /// <summary>
        /// 二次验证：对 SqDiff 候选区域做边缘+标准差检查，过滤背景噪声误报。
        /// </summary>
        private static bool VerifyCandidate(Mat screenGray, Mat templateGray, Mat templateEdges, int templateEdgeCount, Rectangle rect)
        {
            // 边界检查
            if (rect.X < 0 || rect.Y < 0 ||
                rect.X + rect.Width > screenGray.Width ||
                rect.Y + rect.Height > screenGray.Height)
            {
                return false;
            }

            using (Mat roiGray = new Mat(screenGray, new OpenCvSharp.Rect(rect.X, rect.Y, rect.Width, rect.Height)))
            {
                // 检查1：候选区域的标准差不能太低（排除纯色/渐变背景）
                Cv2.MeanStdDev(templateGray, out _, out Scalar templateStd);
                Cv2.MeanStdDev(roiGray, out _, out Scalar roiStd);
                double stdRatio = roiStd.Val0 / Math.Max(1.0, templateStd.Val0);
                if (stdRatio < 0.3)
                {
                    return false;
                }

                // 检查2：边缘结构相似性
                using (Mat roiEdges = new Mat())
                {
                    Cv2.Canny(roiGray, roiEdges, 40, 120);
                    int roiEdgeCount = Cv2.CountNonZero(roiEdges);

                    // 边缘像素数量比例不能差太多
                    if (templateEdgeCount > 20)
                    {
                        double edgeRatio = (double)roiEdgeCount / templateEdgeCount;
                        if (edgeRatio < 0.3 || edgeRatio > 3.0)
                        {
                            return false;
                        }
                    }

                    // 检查3：用 CCoeff 对边缘图做快速验证
                    if (roiEdges.Width == templateEdges.Width && roiEdges.Height == templateEdges.Height)
                    {
                        using (Mat edgeResult = new Mat())
                        {
                            // 直接比较同尺寸的边缘图
                            Cv2.MatchTemplate(roiEdges, templateEdges, edgeResult, TemplateMatchModes.CCoeffNormed);
                            Cv2.MinMaxLoc(edgeResult, out _, out double edgeScore, out _, out _);
                            if (edgeScore < 0.15)
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
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

        // ============ 算法3: HSV 色彩直方图滑窗匹配 ============
        private int TryMatchWithHistogram(Bitmap screenBmp, double threshold)
        {
            using (Mat screenMat = screenBmp.ToMat())
            using (Mat templateMat = image.ToMat())
            using (Mat screenHsv = new Mat())
            using (Mat templateHsv = new Mat())
            {
                Mat screenBgr = screenMat.Channels() == 4 ? screenMat.CvtColor(ColorConversionCodes.BGRA2BGR) : screenMat;
                Mat templateBgr = templateMat.Channels() == 4 ? templateMat.CvtColor(ColorConversionCodes.BGRA2BGR) : templateMat;
                Cv2.CvtColor(screenBgr, screenHsv, ColorConversionCodes.BGR2HSV);
                Cv2.CvtColor(templateBgr, templateHsv, ColorConversionCodes.BGR2HSV);
                if (screenBgr != screenMat) screenBgr.Dispose();
                if (templateBgr != templateMat) templateBgr.Dispose();

                int[] histSize = { 30, 32 };
                Rangef[] ranges = { new Rangef(0, 180), new Rangef(0, 256) };
                int[] channels = { 0, 1 };

                using (Mat templateHist = new Mat())
                {
                    Cv2.CalcHist(new[] { templateHsv }, channels, null, templateHist, 2, histSize, ranges);
                    Cv2.Normalize(templateHist, templateHist, 0, 1, NormTypes.MinMax);

                    double bestScore = 0;
                    Rectangle bestRect = Rectangle.Empty;

                    double[] scales = { 1.0, 0.9, 1.1, 0.8, 1.2, 0.7, 1.3, 0.6, 1.4, 0.5, 1.5 };
                    for (int si = 0; si < scales.Length; si++)
                    {
                        int winW = Math.Max(8, (int)Math.Round(templateMat.Width * scales[si]));
                        int winH = Math.Max(8, (int)Math.Round(templateMat.Height * scales[si]));
                        if (winW > screenHsv.Width || winH > screenHsv.Height) continue;

                        int stepX = Math.Max(1, winW / 4);
                        int stepY = Math.Max(1, winH / 4);

                        for (int y = 0; y <= screenHsv.Height - winH; y += stepY)
                        {
                            for (int x = 0; x <= screenHsv.Width - winW; x += stepX)
                            {
                                using (Mat roi = new Mat(screenHsv, new OpenCvSharp.Rect(x, y, winW, winH)))
                                using (Mat roiHist = new Mat())
                                {
                                    Cv2.CalcHist(new[] { roi }, channels, null, roiHist, 2, histSize, ranges);
                                    Cv2.Normalize(roiHist, roiHist, 0, 1, NormTypes.MinMax);
                                    double score = Cv2.CompareHist(templateHist, roiHist, HistCompMethods.Correl);
                                    LogMatch("Histogram_s" + scales[si].ToString("F2"), score, new Rectangle(x, y, winW, winH), false);

                                    if (score > bestScore)
                                    {
                                        bestScore = score;
                                        bestRect = new Rectangle(x, y, winW, winH);
                                    }
                                }
                            }
                        }
                    }

                    double histThreshold = Math.Max(0.3, threshold - 0.2);
                    if (bestScore >= histThreshold && !bestRect.IsEmpty)
                    {
                        LogMatch("Histogram_BEST", bestScore, bestRect, true);
                        AddNumberedMarker(bestRect);
                        return 1;
                    }

                    if (!bestRect.IsEmpty)
                        LogMatch("Histogram_BEST", bestScore, bestRect, false);
                }
            }

            return 0;
        }

        // ============ 算法4: pHash 多尺度滑窗匹配 ============
        private int TryMatchWithPHash(Bitmap screenBmp, double threshold)
        {
            using (Mat screenMat = screenBmp.ToMat())
            using (Mat templateMat = image.ToMat())
            using (Mat screenGray = new Mat())
            using (Mat templateGray = new Mat())
            {
                ConvertToGray(screenMat, screenGray);
                ConvertToGray(templateMat, templateGray);

                ulong templateHash = ComputePHash(templateGray);
                double bestScore = 0;
                Rectangle bestRect = Rectangle.Empty;

                double[] scales = { 1.0, 0.9, 1.1, 0.8, 1.2, 0.7, 1.3, 0.6, 1.4, 0.5, 1.5 };
                for (int si = 0; si < scales.Length; si++)
                {
                    int winW = Math.Max(8, (int)Math.Round(templateMat.Width * scales[si]));
                    int winH = Math.Max(8, (int)Math.Round(templateMat.Height * scales[si]));
                    if (winW > screenGray.Width || winH > screenGray.Height) continue;

                    int stepX = Math.Max(1, winW / 3);
                    int stepY = Math.Max(1, winH / 3);

                    for (int y = 0; y <= screenGray.Height - winH; y += stepY)
                    {
                        for (int x = 0; x <= screenGray.Width - winW; x += stepX)
                        {
                            using (Mat roi = new Mat(screenGray, new OpenCvSharp.Rect(x, y, winW, winH)))
                            {
                                ulong roiHash = ComputePHash(roi);
                                int hammingDist = HammingDistance(templateHash, roiHash);
                                double score = 1.0 - (hammingDist / 64.0);

                                if (score > bestScore)
                                {
                                    bestScore = score;
                                    bestRect = new Rectangle(x, y, winW, winH);
                                    LogMatch("pHash_s" + scales[si].ToString("F2"), score, bestRect, false);
                                }
                            }
                        }
                    }
                }

                double pHashThreshold = Math.Max(0.6, threshold - 0.1);
                if (bestScore >= pHashThreshold && !bestRect.IsEmpty)
                {
                    LogMatch("pHash_BEST", bestScore, bestRect, true);
                    AddNumberedMarker(bestRect);
                    return 1;
                }

                if (!bestRect.IsEmpty)
                    LogMatch("pHash_BEST", bestScore, bestRect, false);
            }

            return 0;
        }

        private static ulong ComputePHash(Mat gray)
        {
            using (Mat resized = new Mat())
            using (Mat floatMat = new Mat())
            using (Mat dctMat = new Mat())
            {
                Cv2.Resize(gray, resized, new OpenCvSharp.Size(32, 32), 0, 0, InterpolationFlags.Area);
                resized.ConvertTo(floatMat, MatType.CV_64FC1);
                Cv2.Dct(floatMat, dctMat);

                // 取左上 8x8
                double sum = 0;
                double[] vals = new double[64];
                for (int r = 0; r < 8; r++)
                {
                    for (int c = 0; c < 8; c++)
                    {
                        double v = dctMat.At<double>(r, c);
                        vals[r * 8 + c] = v;
                        sum += v;
                    }
                }

                // 去掉 DC 分量
                double avg = (sum - vals[0]) / 63.0;
                ulong hash = 0;
                for (int i = 0; i < 64; i++)
                {
                    if (i == 0) continue;
                    if (vals[i] > avg)
                        hash |= (1UL << i);
                }

                return hash;
            }
        }

        private static int HammingDistance(ulong a, ulong b)
        {
            ulong xor = a ^ b;
            int dist = 0;
            while (xor != 0)
            {
                dist++;
                xor &= (xor - 1);
            }
            return dist;
        }

        // ============ 算法5: Hu 矩形状匹配 ============
        private int TryMatchWithHuMoments(Bitmap screenBmp, double threshold)
        {
            using (Mat screenMat = screenBmp.ToMat())
            using (Mat templateMat = image.ToMat())
            using (Mat screenGray = new Mat())
            using (Mat templateGray = new Mat())
            using (Mat screenBin = new Mat())
            using (Mat templateBin = new Mat())
            {
                ConvertToGray(screenMat, screenGray);
                ConvertToGray(templateMat, templateGray);

                Cv2.Threshold(templateGray, templateBin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                OpenCvSharp.Point[][] templateContours;
                HierarchyIndex[] templateHierarchy;
                Cv2.FindContours(templateBin, out templateContours, out templateHierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                if (templateContours.Length == 0)
                {
                    LogMatch("HuMoments", 0, Rectangle.Empty, false);
                    return 0;
                }

                // 取最大轮廓
                int maxIdx = 0;
                double maxArea = 0;
                for (int i = 0; i < templateContours.Length; i++)
                {
                    double area = Cv2.ContourArea(templateContours[i]);
                    if (area > maxArea)
                    {
                        maxArea = area;
                        maxIdx = i;
                    }
                }

                Moments templateMoments = Cv2.Moments(templateContours[maxIdx]);
                double[] templateHu = templateMoments.HuMoments();

                double bestScore = double.MaxValue;
                Rectangle bestRect = Rectangle.Empty;

                double[] scales = { 1.0, 0.8, 1.2, 0.6, 1.4, 0.5, 1.5 };
                for (int si = 0; si < scales.Length; si++)
                {
                    int winW = Math.Max(8, (int)Math.Round(templateMat.Width * scales[si]));
                    int winH = Math.Max(8, (int)Math.Round(templateMat.Height * scales[si]));
                    if (winW > screenGray.Width || winH > screenGray.Height) continue;

                    int stepX = Math.Max(1, winW / 3);
                    int stepY = Math.Max(1, winH / 3);

                    for (int y = 0; y <= screenGray.Height - winH; y += stepY)
                    {
                        for (int x = 0; x <= screenGray.Width - winW; x += stepX)
                        {
                            using (Mat roi = new Mat(screenGray, new OpenCvSharp.Rect(x, y, winW, winH)))
                            using (Mat roiBin = new Mat())
                            {
                                Cv2.Threshold(roi, roiBin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                                OpenCvSharp.Point[][] roiContours;
                                HierarchyIndex[] roiHierarchy;
                                Cv2.FindContours(roiBin, out roiContours, out roiHierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                                if (roiContours.Length == 0) continue;

                                int roiMaxIdx = 0;
                                double roiMaxArea = 0;
                                for (int ci = 0; ci < roiContours.Length; ci++)
                                {
                                    double area = Cv2.ContourArea(roiContours[ci]);
                                    if (area > roiMaxArea)
                                    {
                                        roiMaxArea = area;
                                        roiMaxIdx = ci;
                                    }
                                }

                                double matchVal = Cv2.MatchShapes(templateContours[maxIdx], roiContours[roiMaxIdx], ShapeMatchModes.I1, 0);
                                // matchVal越小越相似
                                if (matchVal < bestScore)
                                {
                                    bestScore = matchVal;
                                    bestRect = new Rectangle(x, y, winW, winH);
                                }
                            }
                        }
                    }
                }

                double similarity = 1.0 / (1.0 + bestScore);
                LogMatch("HuMoments_BEST", similarity, bestRect, false);

                double huThreshold = Math.Max(0.3, threshold - 0.2);
                if (similarity >= huThreshold && !bestRect.IsEmpty)
                {
                    LogMatch("HuMoments_BEST", similarity, bestRect, true);
                    AddNumberedMarker(bestRect);
                    return 1;
                }
            }

            return 0;
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
