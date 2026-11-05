using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenCaptureTool.FancyZones
{
    /// <summary>
    /// 全屏画布布局编辑器。
    /// 支持自由放置、移动、八方向缩放、拖拽新建、删除、置顶可重叠区域。
    /// </summary>
    public sealed class CanvasEditorForm : PixelExactForm
    {
        private readonly ZoneLayout layout;
        public ZoneLayout EditedLayout => layout;

        private readonly Rectangle monitorBounds;
        private readonly Rectangle workArea;
        private readonly Rectangle localWorkArea;
        private readonly List<Rectangle> zoneRects = new List<Rectangle>();

        private int selectedIndex = -1;
        private int hoverIndex = -1;

        private enum DragMode
        {
            None,
            Move,
            Resize,
            Create
        }

        [Flags]
        private enum ResizeDir
        {
            None = 0,
            Left = 1,
            Right = 2,
            Top = 4,
            Bottom = 8,
            TopLeft = Top | Left,
            TopRight = Top | Right,
            BottomLeft = Bottom | Left,
            BottomRight = Bottom | Right
        }

        private DragMode dragMode = DragMode.None;
        private ResizeDir resizeDir = ResizeDir.None;
        private Point dragStartPt;
        private Rectangle dragStartRect;
        private Rectangle createPreviewRect;

        private const int MinZoneSize = 40;
        private const int HandleSize = 8;
        private const int HandleHitRadius = 7;

        private Panel toolbar = null!;

        private static readonly Color[] Palette =
        {
            Color.FromArgb(130, 60, 130, 255),
            Color.FromArgb(130, 255, 160, 60),
            Color.FromArgb(130, 60, 200, 120),
            Color.FromArgb(130, 200, 60, 180),
            Color.FromArgb(130, 60, 200, 220),
            Color.FromArgb(130, 220, 220, 60),
            Color.FromArgb(130, 180, 80, 80),
            Color.FromArgb(130, 80, 180, 180)
        };

        public CanvasEditorForm(ZoneLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));

            this.layout = layout.Clone();
            this.layout.Kind = ScreenCaptureTool.FancyZones.LayoutKind.Canvas;

            Screen screen = Screen.FromPoint(Cursor.Position);
            monitorBounds = screen.Bounds;
            workArea = screen.WorkingArea;
            localWorkArea = new Rectangle(
                workArea.X - monitorBounds.X,
                workArea.Y - monitorBounds.Y,
                Math.Max(MinZoneSize, workArea.Width),
                Math.Max(MinZoneSize, workArea.Height));

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Opacity = 0.88;
            BackColor = Color.FromArgb(28, 28, 28);
            KeyPreview = true;
            Bounds = monitorBounds;

            BuildToolbar();
            LoadZonesFromModel();

            if (zoneRects.Count > 0)
            {
                selectedIndex = zoneRects.Count - 1;
            }
        }

        private void BuildToolbar()
        {
            toolbar = new Panel
            {
                Height = 44,
                Dock = DockStyle.Bottom,
                BackColor = Color.FromArgb(48, 48, 48)
            };

            var btnSave = new Button
            {
                Text = "保存",
                Size = new Size(76, 32),
                Location = new Point(10, 6),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                TabStop = false
            };
            btnSave.Click += (s, e) => SaveAndClose();

            var btnCancel = new Button
            {
                Text = "取消",
                Size = new Size(76, 32),
                Location = new Point(94, 6),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                TabStop = false
            };
            btnCancel.Click += (s, e) => CancelAndClose();

            var btnAdd = new Button
            {
                Text = "添加区域",
                Size = new Size(86, 32),
                Location = new Point(184, 6),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                TabStop = false
            };
            btnAdd.Click += (s, e) => AddDefaultZone();

            var btnDelete = new Button
            {
                Text = "删除选中",
                Size = new Size(86, 32),
                Location = new Point(278, 6),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                TabStop = false
            };
            btnDelete.Click += (s, e) => DeleteSelectedZone();

            var btnBringFront = new Button
            {
                Text = "置顶选中",
                Size = new Size(86, 32),
                Location = new Point(372, 6),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                TabStop = false
            };
            btnBringFront.Click += (s, e) => BringSelectedToFront();

            var lblHint = new Label
            {
                Text = "点击=选中 | 拖动内部=移动 | 拖边缘/角点=缩放 | 空白处拖拽=新建 | Del=删除 | Enter=保存 | ESC=取消",
                ForeColor = Color.LightGray,
                AutoSize = true,
                Location = new Point(474, 14)
            };

            toolbar.Controls.AddRange(new Control[]
            {
                btnSave, btnCancel, btnAdd, btnDelete, btnBringFront, lblHint
            });
            Controls.Add(toolbar);
        }

        // ── 数据转换与约束 ──

        private void LoadZonesFromModel()
        {
            zoneRects.Clear();

            if (layout.CanvasZones.Count == 0)
            {
                layout.CanvasZones.Add(new ZoneRect(0.15, 0.15, 0.7, 0.7));
            }

            foreach (var z in layout.CanvasZones)
            {
                int x = localWorkArea.X + (int)Math.Round(z.X * localWorkArea.Width);
                int y = localWorkArea.Y + (int)Math.Round(z.Y * localWorkArea.Height);
                int w = (int)Math.Round(z.Width * localWorkArea.Width);
                int h = (int)Math.Round(z.Height * localWorkArea.Height);

                Rectangle r = ClampZoneRect(new Rectangle(x, y, w, h));
                zoneRects.Add(r);
            }

            SyncModelFromZones();
        }

        private void SyncModelFromZones()
        {
            layout.Kind = ScreenCaptureTool.FancyZones.LayoutKind.Canvas;
            layout.CanvasZones.Clear();

            double totalW = Math.Max(1, localWorkArea.Width);
            double totalH = Math.Max(1, localWorkArea.Height);

            foreach (var r in zoneRects)
            {
                Rectangle clamped = ClampZoneRect(r);
                double rx = Math.Clamp((clamped.X - localWorkArea.X) / totalW, 0.0, 1.0);
                double ry = Math.Clamp((clamped.Y - localWorkArea.Y) / totalH, 0.0, 1.0);
                double rw = Math.Clamp(clamped.Width / totalW, 0.0, 1.0 - rx);
                double rh = Math.Clamp(clamped.Height / totalH, 0.0, 1.0 - ry);

                layout.CanvasZones.Add(new ZoneRect(rx, ry, rw, rh));
            }
        }

        private Rectangle ClampZoneRect(Rectangle r)
        {
            int maxW = Math.Max(MinZoneSize, localWorkArea.Width);
            int maxH = Math.Max(MinZoneSize, localWorkArea.Height);

            int w = Math.Clamp(r.Width, MinZoneSize, maxW);
            int h = Math.Clamp(r.Height, MinZoneSize, maxH);

            int x = Math.Clamp(r.X, localWorkArea.Left, localWorkArea.Right - w);
            int y = Math.Clamp(r.Y, localWorkArea.Top, localWorkArea.Bottom - h);

            return new Rectangle(x, y, w, h);
        }

        private Point ClampPointToWorkArea(Point pt)
        {
            int x = Math.Clamp(pt.X, localWorkArea.Left, localWorkArea.Right);
            int y = Math.Clamp(pt.Y, localWorkArea.Top, localWorkArea.Bottom);
            return new Point(x, y);
        }

        // ── 工具栏与快捷键操作 ──

        private void AddDefaultZone()
        {
            int w = Math.Max(MinZoneSize, (int)Math.Round(localWorkArea.Width * 0.35));
            int h = Math.Max(MinZoneSize, (int)Math.Round(localWorkArea.Height * 0.35));
            int offset = (zoneRects.Count % 6) * 24;
            int x = localWorkArea.Left + (localWorkArea.Width - w) / 2 + offset;
            int y = localWorkArea.Top + (localWorkArea.Height - h) / 2 + offset;

            Rectangle r = ClampZoneRect(new Rectangle(x, y, w, h));
            zoneRects.Add(r);
            selectedIndex = zoneRects.Count - 1;
            SyncModelFromZones();
            Invalidate();
        }

        private void DeleteSelectedZone()
        {
            if (selectedIndex < 0 || selectedIndex >= zoneRects.Count) return;
            if (zoneRects.Count <= 1) return; // 至少保留一个区域

            zoneRects.RemoveAt(selectedIndex);
            if (selectedIndex >= zoneRects.Count)
            {
                selectedIndex = zoneRects.Count - 1;
            }
            hoverIndex = -1;
            SyncModelFromZones();
            Invalidate();
        }

        private void BringSelectedToFront()
        {
            if (selectedIndex < 0 || selectedIndex >= zoneRects.Count - 1) return;

            Rectangle r = zoneRects[selectedIndex];
            zoneRects.RemoveAt(selectedIndex);
            zoneRects.Add(r);
            selectedIndex = zoneRects.Count - 1;
            SyncModelFromZones();
            Invalidate();
        }

        private void SaveAndClose()
        {
            SyncModelFromZones();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void CancelAndClose()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter)
            {
                SaveAndClose();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                CancelAndClose();
                return true;
            }
            if (keyData == Keys.Delete)
            {
                DeleteSelectedZone();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                CancelAndClose();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                SaveAndClose();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedZone();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        // ── 鼠标交互 ──

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            Point pt = e.Location;

            // 1. 若已选中区域，优先检查其 8 个缩放手柄与边缘
            if (selectedIndex >= 0 && selectedIndex < zoneRects.Count)
            {
                ResizeDir dir = HitTestResize(zoneRects[selectedIndex], pt);
                if (dir != ResizeDir.None)
                {
                    dragMode = DragMode.Resize;
                    resizeDir = dir;
                    dragStartPt = pt;
                    dragStartRect = zoneRects[selectedIndex];
                    return;
                }
            }

            // 2. 按 Z 序从顶层到底层检测命中的区域
            int hit = HitTestZone(pt);
            if (hit >= 0)
            {
                selectedIndex = hit;
                ResizeDir dir = HitTestResize(zoneRects[selectedIndex], pt);
                if (dir != ResizeDir.None)
                {
                    dragMode = DragMode.Resize;
                    resizeDir = dir;
                }
                else
                {
                    dragMode = DragMode.Move;
                }
                dragStartPt = pt;
                dragStartRect = zoneRects[selectedIndex];
                Invalidate();
                return;
            }

            // 3. 点击空白工作区：取消选中并开始拖拽新建区域
            selectedIndex = -1;
            dragMode = DragMode.Create;
            dragStartPt = ClampPointToWorkArea(pt);
            createPreviewRect = Rectangle.Empty;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point pt = e.Location;

            if (dragMode == DragMode.Move)
            {
                if (selectedIndex >= 0 && selectedIndex < zoneRects.Count)
                {
                    int dx = pt.X - dragStartPt.X;
                    int dy = pt.Y - dragStartPt.Y;
                    Rectangle moved = new Rectangle(
                        dragStartRect.X + dx,
                        dragStartRect.Y + dy,
                        dragStartRect.Width,
                        dragStartRect.Height);
                    zoneRects[selectedIndex] = ClampZoneRect(moved);
                    Invalidate();
                }
                return;
            }

            if (dragMode == DragMode.Resize)
            {
                if (selectedIndex >= 0 && selectedIndex < zoneRects.Count)
                {
                    zoneRects[selectedIndex] = ComputeResizedRect(dragStartRect, resizeDir, dragStartPt, pt);
                    Invalidate();
                }
                return;
            }

            if (dragMode == DragMode.Create)
            {
                Point cur = ClampPointToWorkArea(pt);
                int left = Math.Min(dragStartPt.X, cur.X);
                int top = Math.Min(dragStartPt.Y, cur.Y);
                int right = Math.Max(dragStartPt.X, cur.X);
                int bottom = Math.Max(dragStartPt.Y, cur.Y);
                createPreviewRect = Rectangle.FromLTRB(left, top, right, bottom);
                Invalidate();
                return;
            }

            // 未拖拽时更新光标与悬停状态
            UpdateCursorAndHover(pt);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            if (dragMode == DragMode.Move || dragMode == DragMode.Resize)
            {
                dragMode = DragMode.None;
                resizeDir = ResizeDir.None;
                SyncModelFromZones();
                Invalidate();
                return;
            }

            if (dragMode == DragMode.Create)
            {
                dragMode = DragMode.None;
                if (createPreviewRect.Width >= MinZoneSize && createPreviewRect.Height >= MinZoneSize)
                {
                    Rectangle created = ClampZoneRect(createPreviewRect);
                    zoneRects.Add(created);
                    selectedIndex = zoneRects.Count - 1;
                    SyncModelFromZones();
                }
                createPreviewRect = Rectangle.Empty;
                Invalidate();
            }
        }

        private Rectangle ComputeResizedRect(Rectangle start, ResizeDir dir, Point startPt, Point curPt)
        {
            int left = start.Left;
            int top = start.Top;
            int right = start.Right;
            int bottom = start.Bottom;

            int dx = curPt.X - startPt.X;
            int dy = curPt.Y - startPt.Y;

            if ((dir & ResizeDir.Left) != 0)
            {
                left = Math.Clamp(left + dx, localWorkArea.Left, right - MinZoneSize);
            }
            if ((dir & ResizeDir.Right) != 0)
            {
                right = Math.Clamp(right + dx, left + MinZoneSize, localWorkArea.Right);
            }
            if ((dir & ResizeDir.Top) != 0)
            {
                top = Math.Clamp(top + dy, localWorkArea.Top, bottom - MinZoneSize);
            }
            if ((dir & ResizeDir.Bottom) != 0)
            {
                bottom = Math.Clamp(bottom + dy, top + MinZoneSize, localWorkArea.Bottom);
            }

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private void UpdateCursorAndHover(Point pt)
        {
            if (selectedIndex >= 0 && selectedIndex < zoneRects.Count)
            {
                ResizeDir selDir = HitTestResize(zoneRects[selectedIndex], pt);
                if (selDir != ResizeDir.None)
                {
                    Cursor = GetCursorForResize(selDir);
                    return;
                }
            }

            int hit = HitTestZone(pt);
            if (hit != hoverIndex)
            {
                hoverIndex = hit;
                Invalidate();
            }

            if (hit >= 0)
            {
                ResizeDir dir = HitTestResize(zoneRects[hit], pt);
                Cursor = dir != ResizeDir.None ? GetCursorForResize(dir) : Cursors.SizeAll;
            }
            else
            {
                Cursor = Cursors.Cross;
            }
        }

        // ── 命中测试 ──

        private int HitTestZone(Point pt)
        {
            for (int i = zoneRects.Count - 1; i >= 0; i--)
            {
                if (zoneRects[i].Contains(pt))
                {
                    return i;
                }
            }
            return -1;
        }

        private ResizeDir HitTestResize(Rectangle r, Point pt)
        {
            Rectangle outer = Rectangle.Inflate(r, HandleHitRadius, HandleHitRadius);
            if (!outer.Contains(pt)) return ResizeDir.None;

            bool nearLeft = Math.Abs(pt.X - r.Left) <= HandleHitRadius;
            bool nearRight = Math.Abs(pt.X - r.Right) <= HandleHitRadius;
            bool nearTop = Math.Abs(pt.Y - r.Top) <= HandleHitRadius;
            bool nearBottom = Math.Abs(pt.Y - r.Bottom) <= HandleHitRadius;

            ResizeDir dir = ResizeDir.None;
            if (nearLeft) dir |= ResizeDir.Left;
            else if (nearRight) dir |= ResizeDir.Right;

            if (nearTop) dir |= ResizeDir.Top;
            else if (nearBottom) dir |= ResizeDir.Bottom;

            if (dir != ResizeDir.None) return dir;

            // 检查中点手柄
            Point[] handles = GetHandleCenters(r);
            ResizeDir[] dirs =
            {
                ResizeDir.TopLeft, ResizeDir.Top, ResizeDir.TopRight,
                ResizeDir.Right, ResizeDir.BottomRight, ResizeDir.Bottom,
                ResizeDir.BottomLeft, ResizeDir.Left
            };

            for (int i = 0; i < handles.Length; i++)
            {
                if (Math.Abs(pt.X - handles[i].X) <= HandleHitRadius &&
                    Math.Abs(pt.Y - handles[i].Y) <= HandleHitRadius)
                {
                    return dirs[i];
                }
            }

            return ResizeDir.None;
        }

        private static Point[] GetHandleCenters(Rectangle r)
        {
            int midX = r.Left + r.Width / 2;
            int midY = r.Top + r.Height / 2;
            return new[]
            {
                new Point(r.Left, r.Top),
                new Point(midX, r.Top),
                new Point(r.Right, r.Top),
                new Point(r.Right, midY),
                new Point(r.Right, r.Bottom),
                new Point(midX, r.Bottom),
                new Point(r.Left, r.Bottom),
                new Point(r.Left, midY)
            };
        }

        private static Cursor GetCursorForResize(ResizeDir dir)
        {
            return dir switch
            {
                ResizeDir.TopLeft or ResizeDir.BottomRight => Cursors.SizeNWSE,
                ResizeDir.TopRight or ResizeDir.BottomLeft => Cursors.SizeNESW,
                ResizeDir.Left or ResizeDir.Right => Cursors.SizeWE,
                ResizeDir.Top or ResizeDir.Bottom => Cursors.SizeNS,
                _ => Cursors.Default
            };
        }

        // ── 绘制 ──

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 绘制工作区虚线边框
            using (Pen workPen = new Pen(Color.FromArgb(90, 255, 255, 255), 1) { DashStyle = DashStyle.Dot })
            {
                Rectangle border = new Rectangle(
                    localWorkArea.X,
                    localWorkArea.Y,
                    Math.Max(1, localWorkArea.Width - 1),
                    Math.Max(1, localWorkArea.Height - 1));
                g.DrawRectangle(workPen, border);
            }

            using (Font indexFont = new Font("Segoe UI", 20, FontStyle.Bold))
            using (Font sizeFont = new Font("Segoe UI", 9, FontStyle.Regular))
            using (StringFormat centerFormat = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                // 按顺序绘制（后面的区域在顶层）
                for (int i = 0; i < zoneRects.Count; i++)
                {
                    Rectangle r = zoneRects[i];
                    bool isSelected = i == selectedIndex;
                    bool isHover = i == hoverIndex;

                    Color baseColor = Palette[i % Palette.Length];
                    Color fillColor = baseColor;
                    if (isSelected)
                    {
                        fillColor = Color.FromArgb(175, baseColor.R, baseColor.G, baseColor.B);
                    }
                    else if (isHover)
                    {
                        fillColor = Color.FromArgb(155, baseColor.R, baseColor.G, baseColor.B);
                    }

                    using (SolidBrush fillBrush = new SolidBrush(fillColor))
                    {
                        g.FillRectangle(fillBrush, r);
                    }

                    Color borderColor = isSelected
                        ? Color.Gold
                        : Color.FromArgb(220, 255, 255, 255);
                    float borderWidth = isSelected ? 2.5f : 1.5f;

                    using (Pen borderPen = new Pen(borderColor, borderWidth))
                    {
                        g.DrawRectangle(borderPen, r);
                    }

                    // 区域序号与尺寸信息
                    string label = (i + 1).ToString();
                    RectangleF labelRect = new RectangleF(r.X, r.Y, r.Width, r.Height);
                    g.DrawString(label, indexFont, Brushes.White, labelRect, centerFormat);

                    string dimText = $"{r.Width} × {r.Height}";
                    g.DrawString(dimText, sizeFont, Brushes.WhiteSmoke, r.X + 6, r.Y + 6);
                }

                // 为选中区域绘制 8 个控制手柄
                if (selectedIndex >= 0 && selectedIndex < zoneRects.Count)
                {
                    DrawHandles(g, zoneRects[selectedIndex]);
                }

                // 拖拽新建时的预览框
                if (dragMode == DragMode.Create && createPreviewRect.Width > 0 && createPreviewRect.Height > 0)
                {
                    bool valid = createPreviewRect.Width >= MinZoneSize && createPreviewRect.Height >= MinZoneSize;
                    Color previewColor = valid
                        ? Color.FromArgb(90, 80, 200, 120)
                        : Color.FromArgb(90, 220, 80, 80);
                    Color strokeColor = valid ? Color.LimeGreen : Color.IndianRed;

                    using (SolidBrush pb = new SolidBrush(previewColor))
                    {
                        g.FillRectangle(pb, createPreviewRect);
                    }
                    using (Pen pp = new Pen(strokeColor, 2) { DashStyle = DashStyle.Dash })
                    {
                        g.DrawRectangle(pp, createPreviewRect);
                    }
                }
            }
        }

        private static void DrawHandles(Graphics g, Rectangle r)
        {
            Point[] centers = GetHandleCenters(r);
            int half = HandleSize / 2;

            using (SolidBrush fill = new SolidBrush(Color.Gold))
            using (Pen outline = new Pen(Color.Black, 1.2f))
            {
                foreach (Point pt in centers)
                {
                    Rectangle hr = new Rectangle(pt.X - half, pt.Y - half, HandleSize, HandleSize);
                    g.FillRectangle(fill, hr);
                    g.DrawRectangle(outline, hr);
                }
            }
        }
    }
}
