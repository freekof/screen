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

        private enum HandleType
        {
            None, TopLeft, TopRight, BottomLeft, BottomRight, Top, Bottom, Left, Right, Move
        }

        public Rectangle SelectedRegion => selectedRegion;
        public Bitmap SelectedImage { get; private set; }

        public CaptureForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.DoubleBuffered = true;
            this.Cursor = Cursors.Cross;
            this.TopMost = true;
            this.ShowInTaskbar = false;

            CaptureScreen();
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
                ConfirmSelection();
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

            if (selectedRegion != Rectangle.Empty)
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

            // 绘制放大镜
            DrawMagnifier(g, this.PointToClient(Cursor.Position));
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
            int magSize = 120;
            int zoom = 8;
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
            g.DrawString(info, this.Font, Brushes.Yellow, magRect.Left, magRect.Bottom + 5);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
                return true;
            }
            if (keyData == Keys.Enter)
            {
                ConfirmSelection();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                screenSnapshot?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
