using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ScreenCaptureTool.FancyZones
{
    /// <summary>
    /// 全屏网格布局编辑器。
    /// 点击区域→水平拆分；Shift+点击→垂直拆分；拖动分割线调整比例；
    /// 右键区域选中→工具栏合并。
    /// </summary>
    public sealed class GridEditorForm : PixelExactForm
    {
        private ZoneLayout layout;
        public ZoneLayout EditedLayout => layout;

        private readonly Rectangle workArea;
        private readonly List<Rectangle> zoneRects = new List<Rectangle>();
        private int hoverZone = -1;
        private int dragBoundary = -1; // -1 = none
        private bool dragIsColumn; // true = column, false = row
        private int dragStartPos;
        private readonly HashSet<int> selectedZones = new HashSet<int>();
        private bool mouseDownForSplit;
        private Point mouseDownPoint;
        private const int SplitterHitSize = 6;
        private const int MinCellPercent = 3; // 最小 3%
        private Panel toolbar;

        public GridEditorForm(ZoneLayout layout)
        {
            this.layout = layout.Clone();
            this.layout.Normalize();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Opacity = 0.85;
            BackColor = Color.FromArgb(30, 30, 30);
            KeyPreview = true;

            Screen screen = Screen.FromPoint(Cursor.Position);
            Rectangle monitorBounds = screen.Bounds;
            Rectangle screenWorkArea = screen.WorkingArea;
            workArea = new Rectangle(
                screenWorkArea.X - monitorBounds.X,
                screenWorkArea.Y - monitorBounds.Y,
                screenWorkArea.Width,
                screenWorkArea.Height);
            Bounds = monitorBounds;

            BuildToolbar();
            RecalcZones();
        }

        private void BuildToolbar()
        {
            toolbar = new Panel
            {
                Height = 44,
                Dock = DockStyle.Bottom,
                BackColor = Color.FromArgb(50, 50, 50)
            };

            var btnSave = new Button { Text = "保存", Size = new Size(80, 32), Location = new Point(10, 6), FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
            btnSave.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };

            var btnCancel = new Button { Text = "取消", Size = new Size(80, 32), Location = new Point(100, 6), FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            var btnMerge = new Button { Text = "合并选中", Size = new Size(90, 32), Location = new Point(200, 6), FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
            btnMerge.Click += BtnMerge_Click;

            var lblHint = new Label
            {
                Text = "点击=水平拆分 | Shift+点击=垂直拆分 | 拖分割线=调整 | 右键=选中 | Delete=合并到相邻 | ESC=取消",
                ForeColor = Color.LightGray,
                AutoSize = true,
                Location = new Point(310, 14)
            };

            toolbar.Controls.AddRange(new Control[] { btnSave, btnCancel, btnMerge, lblHint });
            Controls.Add(toolbar);
        }

        private void RecalcZones()
        {
            zoneRects.Clear();
            zoneRects.AddRange(layout.GetZoneRects(workArea, 4));
        }

        // ── 鼠标事件 ──

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Point pt = e.Location;

            if (e.Button == MouseButtons.Right)
            {
                // 右键选中/取消
                int zone = HitTestZone(pt);
                if (zone >= 0)
                {
                    if (selectedZones.Contains(zone)) selectedZones.Remove(zone);
                    else selectedZones.Add(zone);
                    Invalidate();
                }
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            // 先检查是否在分割线上
            int boundary = HitTestBoundary(pt, out bool isCol);
            if (boundary >= 0)
            {
                dragBoundary = boundary;
                dragIsColumn = isCol;
                dragStartPos = isCol ? pt.X : pt.Y;
                return;
            }

            // 否则是拆分
            mouseDownForSplit = true;
            mouseDownPoint = pt;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Point pt = e.Location;

            if (dragBoundary >= 0)
            {
                DragBoundary(pt);
                return;
            }

            // 更新光标
            int boundary = HitTestBoundary(pt, out bool isCol);
            if (boundary >= 0)
            {
                Cursor = isCol ? Cursors.SizeWE : Cursors.SizeNS;
            }
            else
            {
                Cursor = Cursors.Default;
            }

            int zone = HitTestZone(pt);
            if (zone != hoverZone)
            {
                hoverZone = zone;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragBoundary >= 0)
            {
                dragBoundary = -1;
                return;
            }

            if (mouseDownForSplit && e.Button == MouseButtons.Left)
            {
                mouseDownForSplit = false;
                Point pt = e.Location;
                // 小范围移动才算点击
                if (Math.Abs(pt.X - mouseDownPoint.X) < 8 && Math.Abs(pt.Y - mouseDownPoint.Y) < 8)
                {
                    int zone = HitTestZone(pt);
                    if (zone >= 0)
                    {
                        bool vertical = (ModifierKeys & Keys.Shift) != 0;
                        SplitZone(zone, pt, vertical);
                    }
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (e.KeyCode == Keys.Delete)
            {
                MergeSelectionWithNeighbor();
            }
        }

        // ── 绘制 ──

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color[] palette = {
                Color.FromArgb(120, 60, 130, 255), Color.FromArgb(120, 255, 160, 60),
                Color.FromArgb(120, 60, 200, 120), Color.FromArgb(120, 200, 60, 180),
                Color.FromArgb(120, 60, 200, 220), Color.FromArgb(120, 220, 220, 60),
                Color.FromArgb(120, 180, 80, 80), Color.FromArgb(120, 80, 180, 180)
            };

            for (int i = 0; i < zoneRects.Count; i++)
            {
                Rectangle r = zoneRects[i];
                bool sel = selectedZones.Contains(i);
                bool hover = i == hoverZone;

                Color fill = palette[i % palette.Length];
                if (sel) fill = Color.FromArgb(160, 255, 200, 60);
                else if (hover) fill = Color.FromArgb(fill.A + 40, fill.R, fill.G, fill.B);

                using (SolidBrush fb = new SolidBrush(fill))
                {
                    g.FillRectangle(fb, r);
                }
                using (Pen pen = new Pen(sel ? Color.Yellow : Color.White, sel ? 3 : 1))
                {
                    g.DrawRectangle(pen, r);
                }

                using (Font font = new Font("Segoe UI", 20, FontStyle.Bold))
                using (StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    g.DrawString((i + 1).ToString(), font, Brushes.White,
                        new RectangleF(r.X, r.Y, r.Width, r.Height), sf);
                }
            }
        }

        // ── 拆分 ──

        private void SplitZone(int zoneIdx, Point clickPt, bool vertical)
        {
            int rows = layout.CellMap.Count;
            int cols = layout.CellMap[0].Count;
            int minR = rows, minC = cols, maxR = -1, maxC = -1;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (layout.CellMap[r][c] == zoneIdx)
                    {
                        minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
                        minC = Math.Min(minC, c); maxC = Math.Max(maxC, c);
                    }
                }
            }
            if (maxR < 0) return;

            Rectangle zoneRect = zoneIdx >= 0 && zoneIdx < zoneRects.Count
                ? zoneRects[zoneIdx]
                : Rectangle.Empty;
            if (zoneRect.IsEmpty) return;

            int coordinate = vertical ? clickPt.X : clickPt.Y;
            int zoneStart = vertical ? zoneRect.Left : zoneRect.Top;
            int zoneEnd = vertical ? zoneRect.Right : zoneRect.Bottom;
            int inset = Math.Max(8, (zoneEnd - zoneStart) / 5);
            coordinate = Math.Clamp(coordinate, zoneStart + inset, zoneEnd - inset);

            int newZone = layout.ZoneCount;
            if (vertical)
            {
                double[] colStarts = PrefixPixels(layout.ColumnPercents, workArea.X, workArea.Width);
                int boundary = -1;
                for (int c = minC + 1; c <= maxC; c++)
                {
                    if (Math.Abs(colStarts[c] - coordinate) <= SplitterHitSize)
                    {
                        boundary = c;
                        break;
                    }
                }

                if (boundary >= 0)
                {
                    for (int r = minR; r <= maxR; r++)
                    {
                        for (int c = boundary; c <= maxC; c++)
                        {
                            if (layout.CellMap[r][c] == zoneIdx)
                                layout.CellMap[r][c] = newZone;
                        }
                    }
                }
                else
                {
                    int sourceCol = -1;
                    for (int c = minC; c <= maxC; c++)
                    {
                        if (coordinate > colStarts[c] && coordinate < colStarts[c + 1])
                        {
                            sourceCol = c;
                            break;
                        }
                    }
                    if (sourceCol < 0) return;

                    double fraction = (coordinate - colStarts[sourceCol]) /
                                      (colStarts[sourceCol + 1] - colStarts[sourceCol]);
                    fraction = Math.Clamp(fraction, 0.05, 0.95);
                    double originalPercent = layout.ColumnPercents[sourceCol];
                    double leftPercent = originalPercent * fraction;
                    double rightPercent = originalPercent - leftPercent;
                    if (leftPercent < 0.01 || rightPercent < 0.01) return;

                    layout.ColumnPercents[sourceCol] = leftPercent;
                    layout.ColumnPercents.Insert(sourceCol + 1, rightPercent);

                    for (int r = 0; r < rows; r++)
                    {
                        int cell = layout.CellMap[r][sourceCol];
                        layout.CellMap[r].Insert(sourceCol + 1, cell);
                    }

                    for (int r = minR; r <= maxR; r++)
                    {
                        for (int c = sourceCol + 1; c <= maxC + 1; c++)
                        {
                            if (layout.CellMap[r][c] == zoneIdx)
                                layout.CellMap[r][c] = newZone;
                        }
                    }
                }
            }
            else
            {
                double[] rowStarts = PrefixPixels(layout.RowPercents, workArea.Y, workArea.Height);
                int boundary = -1;
                for (int r = minR + 1; r <= maxR; r++)
                {
                    if (Math.Abs(rowStarts[r] - coordinate) <= SplitterHitSize)
                    {
                        boundary = r;
                        break;
                    }
                }

                if (boundary >= 0)
                {
                    for (int r = boundary; r <= maxR; r++)
                    {
                        for (int c = minC; c <= maxC; c++)
                        {
                            if (layout.CellMap[r][c] == zoneIdx)
                                layout.CellMap[r][c] = newZone;
                        }
                    }
                }
                else
                {
                    int sourceRow = -1;
                    for (int r = minR; r <= maxR; r++)
                    {
                        if (coordinate > rowStarts[r] && coordinate < rowStarts[r + 1])
                        {
                            sourceRow = r;
                            break;
                        }
                    }
                    if (sourceRow < 0) return;

                    double fraction = (coordinate - rowStarts[sourceRow]) /
                                      (rowStarts[sourceRow + 1] - rowStarts[sourceRow]);
                    fraction = Math.Clamp(fraction, 0.05, 0.95);
                    double originalPercent = layout.RowPercents[sourceRow];
                    double topPercent = originalPercent * fraction;
                    double bottomPercent = originalPercent - topPercent;
                    if (topPercent < 0.01 || bottomPercent < 0.01) return;

                    layout.RowPercents[sourceRow] = topPercent;
                    layout.RowPercents.Insert(sourceRow + 1, bottomPercent);
                    layout.CellMap.Insert(sourceRow + 1, new List<int>(layout.CellMap[sourceRow]));

                    for (int c = minC; c <= maxC; c++)
                    {
                        if (layout.CellMap[sourceRow + 1][c] == zoneIdx)
                            layout.CellMap[sourceRow + 1][c] = newZone;
                    }
                    for (int r = sourceRow + 2; r <= maxR + 1; r++)
                    {
                        for (int c = minC; c <= maxC; c++)
                        {
                            if (layout.CellMap[r][c] == zoneIdx)
                                layout.CellMap[r][c] = newZone;
                        }
                    }
                }
            }

            RenumberZones();
            layout.Normalize();
            RecalcZones();
            selectedZones.Clear();
            Invalidate();
        }

        // ── 分割线拖动 ──

        private void DragBoundary(Point pt)
        {
            if (dragIsColumn)
            {
                double totalW = workArea.Width;
                double delta = (pt.X - dragStartPos) / totalW;
                if (dragBoundary > 0 && dragBoundary < layout.ColumnPercents.Count)
                {
                    double left = layout.ColumnPercents[dragBoundary - 1];
                    double right = layout.ColumnPercents[dragBoundary];
                    double minP = MinCellPercent / 100.0;
                    if (left + delta >= minP && right - delta >= minP)
                    {
                        layout.ColumnPercents[dragBoundary - 1] = left + delta;
                        layout.ColumnPercents[dragBoundary] = right - delta;
                        dragStartPos = pt.X;
                    }
                }
            }
            else
            {
                double totalH = workArea.Height;
                double delta = (pt.Y - dragStartPos) / totalH;
                if (dragBoundary > 0 && dragBoundary < layout.RowPercents.Count)
                {
                    double top = layout.RowPercents[dragBoundary - 1];
                    double bottom = layout.RowPercents[dragBoundary];
                    double minP = MinCellPercent / 100.0;
                    if (top + delta >= minP && bottom - delta >= minP)
                    {
                        layout.RowPercents[dragBoundary - 1] = top + delta;
                        layout.RowPercents[dragBoundary] = bottom - delta;
                        dragStartPos = pt.Y;
                    }
                }
            }
            RecalcZones();
            Invalidate();
        }

        // ── 合并 ──

        private void MergeSelectionWithNeighbor()
        {
            if (selectedZones.Count >= 2)
            {
                BtnMerge_Click(null, EventArgs.Empty);
                return;
            }
            if (selectedZones.Count != 1) return;

            int selected = selectedZones.First();
            int rows = layout.CellMap.Count;
            int cols = layout.CellMap[0].Count;
            var contacts = new Dictionary<int, int>();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (layout.CellMap[r][c] != selected) continue;
                    AddNeighborContact(r - 1, c);
                    AddNeighborContact(r + 1, c);
                    AddNeighborContact(r, c - 1);
                    AddNeighborContact(r, c + 1);
                }
            }

            if (contacts.Count == 0) return;
            int neighbor = contacts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
            selectedZones.Add(neighbor);
            BtnMerge_Click(null, EventArgs.Empty);

            void AddNeighborContact(int r, int c)
            {
                if (r < 0 || r >= rows || c < 0 || c >= cols) return;
                int zone = layout.CellMap[r][c];
                if (zone == selected) return;
                contacts.TryGetValue(zone, out int count);
                contacts[zone] = count + 1;
            }
        }

        private void BtnMerge_Click(object? sender, EventArgs e)
        {
            if (selectedZones.Count < 2) return;
            int rows = layout.CellMap.Count;
            int cols = layout.CellMap[0].Count;

            // 计算选中区域的边界
            int minR = rows, minC = cols, maxR = -1, maxC = -1;
            foreach (int z in selectedZones)
            {
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (layout.CellMap[r][c] == z)
                        {
                            minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
                            minC = Math.Min(minC, c); maxC = Math.Max(maxC, c);
                        }
                    }
                }
            }
            if (maxR < 0) return;

            // 扩展到闭包
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int r = minR; r <= maxR; r++)
                {
                    for (int c = minC; c <= maxC; c++)
                    {
                        int z = layout.CellMap[r][c];
                        if (selectedZones.Contains(z)) continue;
                        // 这个单元格被包含在外接矩形内但不在选中列表 → 扩展
                        // 找到这个区域的全部范围
                        for (int rr = 0; rr < rows; rr++)
                        {
                            for (int cc = 0; cc < cols; cc++)
                            {
                                if (layout.CellMap[rr][cc] == z)
                                {
                                    if (rr < minR) { minR = rr; changed = true; }
                                    if (rr > maxR) { maxR = rr; changed = true; }
                                    if (cc < minC) { minC = cc; changed = true; }
                                    if (cc > maxC) { maxC = cc; changed = true; }
                                }
                            }
                        }
                        selectedZones.Add(z);
                    }
                }
            }

            // 统一赋值为最小 zone index
            int targetZone = selectedZones.Min();
            for (int r = minR; r <= maxR; r++)
            {
                for (int c = minC; c <= maxC; c++)
                {
                    layout.CellMap[r][c] = targetZone;
                }
            }

            // 重编号 zone indices
            RenumberZones();
            layout.Normalize();
            CleanupGrid();
            RecalcZones();
            selectedZones.Clear();
            Invalidate();
        }

        private void RenumberZones()
        {
            var mapping = new Dictionary<int, int>();
            int next = 0;
            for (int r = 0; r < layout.CellMap.Count; r++)
            {
                for (int c = 0; c < layout.CellMap[r].Count; c++)
                {
                    int old = layout.CellMap[r][c];
                    if (!mapping.ContainsKey(old))
                    {
                        mapping[old] = next++;
                    }
                    layout.CellMap[r][c] = mapping[old];
                }
            }
        }

        private void CleanupGrid()
        {
            // 合并相同的相邻行
            for (int r = layout.CellMap.Count - 1; r > 0; r--)
            {
                if (layout.CellMap[r].SequenceEqual(layout.CellMap[r - 1]))
                {
                    layout.RowPercents[r - 1] += layout.RowPercents[r];
                    layout.RowPercents.RemoveAt(r);
                    layout.CellMap.RemoveAt(r);
                }
            }
            // 合并相同的相邻列
            int cols = layout.CellMap[0].Count;
            for (int c = cols - 1; c > 0; c--)
            {
                bool same = true;
                for (int r = 0; r < layout.CellMap.Count; r++)
                {
                    if (layout.CellMap[r][c] != layout.CellMap[r][c - 1])
                    {
                        same = false;
                        break;
                    }
                }
                if (same)
                {
                    layout.ColumnPercents[c - 1] += layout.ColumnPercents[c];
                    layout.ColumnPercents.RemoveAt(c);
                    for (int r = 0; r < layout.CellMap.Count; r++)
                    {
                        layout.CellMap[r].RemoveAt(c);
                    }
                }
            }
        }

        // ── 命中测试 ──

        private int HitTestZone(Point pt)
        {
            for (int i = 0; i < zoneRects.Count; i++)
            {
                if (zoneRects[i].Contains(pt)) return i;
            }
            return -1;
        }

        private int HitTestBoundary(Point pt, out bool isColumn)
        {
            isColumn = true;
            // 列边界
            double[] colPixels = PrefixPixels(layout.ColumnPercents, workArea.X, workArea.Width);
            for (int i = 1; i < colPixels.Length - 1; i++)
            {
                bool sharedBoundary = false;
                for (int r = 0; r < layout.CellMap.Count; r++)
                {
                    if (layout.CellMap[r][i - 1] != layout.CellMap[r][i])
                    {
                        sharedBoundary = true;
                        break;
                    }
                }
                if (sharedBoundary && Math.Abs(pt.X - colPixels[i]) <= SplitterHitSize && pt.Y >= workArea.Top && pt.Y <= workArea.Bottom)
                {
                    isColumn = true;
                    return i;
                }
            }
            // 行边界
            double[] rowPixels = PrefixPixels(layout.RowPercents, workArea.Y, workArea.Height);
            for (int i = 1; i < rowPixels.Length - 1; i++)
            {
                bool sharedBoundary = false;
                for (int c = 0; c < layout.CellMap[i - 1].Count; c++)
                {
                    if (layout.CellMap[i - 1][c] != layout.CellMap[i][c])
                    {
                        sharedBoundary = true;
                        break;
                    }
                }
                if (sharedBoundary && Math.Abs(pt.Y - rowPixels[i]) <= SplitterHitSize && pt.X >= workArea.Left && pt.X <= workArea.Right)
                {
                    isColumn = false;
                    return i;
                }
            }
            return -1;
        }

        private static double[] PrefixPixels(List<double> percents, int origin, int totalSize)
        {
            double sum = percents.Sum();
            if (sum <= 0) sum = 1;
            var arr = new double[percents.Count + 1];
            arr[0] = origin;
            for (int i = 0; i < percents.Count; i++)
            {
                arr[i + 1] = arr[i] + (percents[i] / sum) * totalSize;
            }
            arr[percents.Count] = origin + totalSize;
            return arr;
        }
    }
}
