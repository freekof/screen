using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenCaptureTool
{
    public class CaptureForm : Form
    {
        private Bitmap screenSnapshot;
        private Rectangle selectedRegion = Rectangle.Empty;
        private Point startPoint;
        private bool isSelecting = false;
        private bool isAdjusting = false;
        private int handleSize = 8;
        private HandleType activeHandle = HandleType.None;
        private readonly Settings settings;
        private readonly bool stampMode;
        private float stampScale = 1.0f;
        private Point currentMouse;
        private Font magnifierFont;
        private Timer followTimer;

        private enum HandleType
        {
            None, TopLeft, TopRight, BottomLeft, BottomRight, Top, Bottom, Left, Right, Move
        }

        public Rectangle SelectedRegion => selectedRegion;
        public Bitmap SelectedImage { get; private set; }

        public CaptureForm(Settings settings)
        {
            this.settings = settings;
            this.stampMode = settings.StampModeEnabled;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;
            this.TopMost = true;
            this.ShowInTaskbar = false;

            magnifierFont = new Font(this.Font.FontFamily, settings.MagnifierFontSize, FontStyle.Regular);

            CaptureScreen();
            currentMouse = this.PointToClient(Cursor.Position);
            StartFollowTimer();
        }

        private void StartFollowTimer()
        {
            followTimer = new Timer();
            followTimer.Interval = 16;
            followTimer.Tick += (s, e) =>
            {
                currentMouse = this.PointToClient(Cursor.Position);
                this.Invalidate();
            };
            followTimer.Start();
        }

        private void CaptureScreen()
        {
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            screenSnapshot = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics g = Graphics.FromImage(screenSnapshot))
            {
                g.CopyFromScreen(0, 0, 0, 0, bounds.Size);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (stampMode)
                {
                    selectedRegion = GetStampRect();
                    ConfirmSelection();
                    return;
                }
                activeHandle = GetHandleAtPoint(e.Location);
                if (activeHandle != HandleType.None)
                {
                    isAdjusting = true;
                    startPoint = e.Location;
                }
                else
                {
                    isSelecting = true;
                    isAdjusting = false;
                    startPoint = e.Location;
                    selectedRegion = new Rectangle(e.Location, new Size(0, 0));
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (stampMode)
                {
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
                else
                {
                    ConfirmSelection();
                }
            }
        }

        private void ConfirmSelection()
        {
            if (selectedRegion.Width > 5 && selectedRegion.Height > 5)
            {
                SelectedImage = screenSnapshot.Clone(selectedRegion, screenSnapshot.PixelFormat);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            currentMouse = e.Location;
            if (isSelecting)
            {
                int x = Math.Min(startPoint.X, e.X);
                int y = Math.Min(startPoint.Y, e.Y);
                int width = Math.Abs(startPoint.X - e.X);
                int height = Math.Abs(startPoint.Y - e.Y);
                selectedRegion = new Rectangle(x, y, width, height);
            }
            else if (isAdjusting)
            {
                AdjustRegion(e.Location);
            }
            else
            {
                UpdateCursor(e.Location);
            }
            this.Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!stampMode)
            {
                base.OnMouseWheel(e);
                return;
            }

            float step = Math.Max(1, settings.StampWheelScaleStepPercent) / 100.0f;
            stampScale = e.Delta > 0 ? stampScale + step : stampScale - step;
            stampScale = Math.Max(0.2f, Math.Min(5.0f, stampScale));
            currentMouse = e.Location;
            this.Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            isSelecting = false;
            isAdjusting = false;
            activeHandle = HandleType.None;
        }

        private void AdjustRegion(Point currentPos)
        {
            int dx = currentPos.X - startPoint.X;
            int dy = currentPos.Y - startPoint.Y;

            int left = selectedRegion.Left;
            int top = selectedRegion.Top;
            int right = selectedRegion.Right;
            int bottom = selectedRegion.Bottom;

            switch (activeHandle)
            {
                case HandleType.TopLeft:
                    left += dx; top += dy; break;
                case HandleType.TopRight:
                    right += dx; top += dy; break;
                case HandleType.BottomLeft:
                    left += dx; bottom += dy; break;
                case HandleType.BottomRight:
                    right += dx; bottom += dy; break;
                case HandleType.Top:
                    top += dy; break;
                case HandleType.Bottom:
                    bottom += dy; break;
                case HandleType.Left:
                    left += dx; break;
                case HandleType.Right:
                    right += dx; break;
                case HandleType.Move:
                    left += dx; right += dx; top += dy; bottom += dy; break;
            }

            selectedRegion = Rectangle.FromLTRB(
                Math.Min(left, right), Math.Min(top, bottom),
                Math.Max(left, right), Math.Max(top, bottom));
            
            startPoint = currentPos;
        }

        private HandleType GetHandleAtPoint(Point p)
        {
            if (selectedRegion == Rectangle.Empty) return HandleType.None;

            if (GetHandleRect(selectedRegion.Left, selectedRegion.Top).Contains(p)) return HandleType.TopLeft;
            if (GetHandleRect(selectedRegion.Right, selectedRegion.Top).Contains(p)) return HandleType.TopRight;
            if (GetHandleRect(selectedRegion.Left, selectedRegion.Bottom).Contains(p)) return HandleType.BottomLeft;
            if (GetHandleRect(selectedRegion.Right, selectedRegion.Bottom).Contains(p)) return HandleType.BottomRight;
            if (GetHandleRect(selectedRegion.Left + selectedRegion.Width / 2, selectedRegion.Top).Contains(p)) return HandleType.Top;
            if (GetHandleRect(selectedRegion.Left + selectedRegion.Width / 2, selectedRegion.Bottom).Contains(p)) return HandleType.Bottom;
            if (GetHandleRect(selectedRegion.Left, selectedRegion.Top + selectedRegion.Height / 2).Contains(p)) return HandleType.Left;
            if (GetHandleRect(selectedRegion.Right, selectedRegion.Top + selectedRegion.Height / 2).Contains(p)) return HandleType.Right;
            
            if (selectedRegion.Contains(p)) return HandleType.Move;

            return HandleType.None;
        }

        private Rectangle GetHandleRect(int x, int y)
        {
            return new Rectangle(x - handleSize / 2, y - handleSize / 2, handleSize, handleSize);
        }

        private void UpdateCursor(Point p)
        {
            HandleType handle = GetHandleAtPoint(p);
            switch (handle)
            {
                case HandleType.TopLeft:
                case HandleType.BottomRight:
                    this.Cursor = Cursors.SizeNWSE; break;
                case HandleType.TopRight:
                case HandleType.BottomLeft:
                    this.Cursor = Cursors.SizeNESW; break;
                case HandleType.Top:
                case HandleType.Bottom:
                    this.Cursor = Cursors.SizeNS; break;
                case HandleType.Left:
                case HandleType.Right:
                    this.Cursor = Cursors.SizeWE; break;
                case HandleType.Move:
                    this.Cursor = Cursors.SizeAll; break;
                default:
                    this.Cursor = Cursors.Cross; break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            // 绘制背景快照
            g.DrawImage(screenSnapshot, 0, 0);

            // 绘制半透明遮罩
            using (SolidBrush overlayBrush = new SolidBrush(Color.FromArgb(120, Color.Black)))
            {
                Region region = new Region(this.ClientRectangle);
                if (selectedRegion != Rectangle.Empty)
                {
                    region.Exclude(selectedRegion);
                }
                g.FillRegion(overlayBrush, region);
            }

            if (!stampMode && selectedRegion != Rectangle.Empty)
            {
                // 绘制选区边框
                using (Pen pen = new Pen(Color.Cyan, 2))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawRectangle(pen, selectedRegion);
                }

                // 绘制控制点
                DrawHandles(g);
            }

            if (stampMode)
            {
                DrawStampBox(g);
                DrawStampInfo(g);
            }

            // 绘制放大镜
            DrawMagnifier(g, this.PointToClient(Cursor.Position));
        }

        private Rectangle GetStampRect()
        {
            int width = (int)Math.Round(settings.StampBoxWidth * stampScale);
            int height = (int)Math.Round(settings.StampBoxHeight * stampScale);
            int x = currentMouse.X - width / 2;
            int y = currentMouse.Y - height / 2;
            Rectangle rect = new Rectangle(x, y, width, height);
            rect.Intersect(new Rectangle(0, 0, screenSnapshot.Width, screenSnapshot.Height));
            return rect;
        }

        private void DrawStampBox(Graphics g)
        {
            Rectangle rect = GetStampRect();
            using (Pen pen = new Pen(Color.Orange, 2))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawRectangle(pen, rect);
            }
        }

        private void DrawStampInfo(Graphics g)
        {
            Rectangle rect = GetStampRect();
            string info = $"印章: {rect.Width} x {rect.Height}";
            SizeF size = g.MeasureString(info, magnifierFont);
            RectangleF box = new RectangleF(10, this.Height - size.Height - 20, size.Width + 10, size.Height + 6);
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(140, Color.Black)))
            using (SolidBrush fg = new SolidBrush(Color.White))
            {
                g.FillRectangle(bg, box);
                g.DrawString(info, magnifierFont, fg, box.Left + 5, box.Top + 3);
            }
        }

        private void DrawHandles(Graphics g)
        {
            using (SolidBrush brush = new SolidBrush(Color.Cyan))
            {
                g.FillRectangle(brush, GetHandleRect(selectedRegion.Left, selectedRegion.Top));
                g.FillRectangle(brush, GetHandleRect(selectedRegion.Right, selectedRegion.Top));
                g.FillRectangle(brush, GetHandleRect(selectedRegion.Left, selectedRegion.Bottom));
                g.FillRectangle(brush, GetHandleRect(selectedRegion.Right, selectedRegion.Bottom));
                g.FillRectangle(brush, GetHandleRect(selectedRegion.Left + selectedRegion.Width / 2, selectedRegion.Top));
                g.FillRectangle(brush, GetHandleRect(selectedRegion.Left + selectedRegion.Width / 2, selectedRegion.Bottom));
                g.FillRectangle(brush, GetHandleRect(selectedRegion.Left, selectedRegion.Top + selectedRegion.Height / 2));
                g.FillRectangle(brush, GetHandleRect(selectedRegion.Right, selectedRegion.Top + selectedRegion.Height / 2));
            }
        }

        private void DrawMagnifier(Graphics g, Point mousePos)
        {
            int magSize = Math.Max(60, settings.MagnifierSize);
            int zoom = Math.Max(2, settings.MagnifierZoom);
            int sourceSize = magSize / zoom;
            
            // 放大镜位置（偏移鼠标一段距离，避免遮挡）
            int offsetX = 20;
            int offsetY = 20;
            if (mousePos.X + offsetX + magSize > this.Width) offsetX = -magSize - 20;
            if (mousePos.Y + offsetY + magSize > this.Height) offsetY = -magSize - 20;

            Rectangle magRect = new Rectangle(mousePos.X + offsetX, mousePos.Y + offsetY, magSize, magSize);
            Rectangle srcRect = new Rectangle(mousePos.X - sourceSize / 2, mousePos.Y - sourceSize / 2, sourceSize, sourceSize);

            // 绘制放大镜背景
            g.FillRectangle(Brushes.Black, magRect);
            
            // 确保源矩形在屏幕范围内
            srcRect.Intersect(new Rectangle(0, 0, screenSnapshot.Width, screenSnapshot.Height));
            if (srcRect.Width > 0 && srcRect.Height > 0)
            {
                g.DrawImage(screenSnapshot, magRect, srcRect, GraphicsUnit.Pixel);
            }

            // 绘制放大镜边框和十字准星
            using (Pen pen = new Pen(Color.White, 1))
            {
                g.DrawRectangle(pen, magRect);
                // 十字准星
                g.DrawLine(pen, magRect.Left + magSize / 2, magRect.Top, magRect.Left + magSize / 2, magRect.Bottom);
                g.DrawLine(pen, magRect.Left, magRect.Top + magSize / 2, magRect.Right, magRect.Top + magSize / 2);
            }

            // 显示坐标和颜色信息
            int px = Math.Clamp(mousePos.X, 0, screenSnapshot.Width - 1);
            int py = Math.Clamp(mousePos.Y, 0, screenSnapshot.Height - 1);
            Color pixelColor = screenSnapshot.GetPixel(px, py);
            string info = $"X: {px}, Y: {py}\nRGB: ({pixelColor.R},{pixelColor.G},{pixelColor.B})";
            g.DrawString(info, magnifierFont, Brushes.Yellow, magRect.Left, magRect.Bottom + 5);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (IsCancelHotkey(keyData))
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                return true;
            }
            if (HandleWASDMove(keyData))
            {
                return true;
            }
            if (keyData == Keys.Enter)
            {
                ConfirmSelection();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool HandleWASDMove(Keys keyData)
        {
            int dx = 0;
            int dy = 0;
            switch (keyData)
            {
                case Keys.W:
                    dy = -1; break;
                case Keys.A:
                    dx = -1; break;
                case Keys.S:
                    dy = 1; break;
                case Keys.D:
                    dx = 1; break;
                default:
                    return false;
            }

            System.Drawing.Point pos = Cursor.Position;
            Rectangle bounds = Screen.PrimaryScreen.Bounds;
            int newX = Math.Max(bounds.Left, Math.Min(bounds.Right - 1, pos.X + dx));
            int newY = Math.Max(bounds.Top, Math.Min(bounds.Bottom - 1, pos.Y + dy));
            Cursor.Position = new System.Drawing.Point(newX, newY);
            currentMouse = this.PointToClient(Cursor.Position);
            this.Invalidate();
            return true;
        }

        private bool IsCancelHotkey(Keys keyData)
        {
            Keys expected = (Keys)settings.CancelHotkeyCode;
            if ((settings.CancelHotkeyModifiers & 1) != 0) expected |= Keys.Alt;
            if ((settings.CancelHotkeyModifiers & 2) != 0) expected |= Keys.Control;
            if ((settings.CancelHotkeyModifiers & 4) != 0) expected |= Keys.Shift;
            return keyData == expected;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                screenSnapshot?.Dispose();
                magnifierFont?.Dispose();
                if (followTimer != null)
                {
                    followTimer.Stop();
                    followTimer.Dispose();
                    followTimer = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
