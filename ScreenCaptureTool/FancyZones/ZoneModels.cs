using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace ScreenCaptureTool.FancyZones
{
    /// <summary>布局类型：网格（不可重叠，按行列比例划分）或画布（自由矩形，可重叠）。</summary>
    public enum LayoutKind
    {
        Grid = 0,
        Canvas = 1
    }

    /// <summary>画布布局中的单个区域，坐标为相对工作区的比例；允许越出 0..1 范围。</summary>
    public sealed class ZoneRect
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string TargetApp { get; set; } = "";

        public ZoneRect() { }

        public ZoneRect(double x, double y, double width, double height, string targetApp = "")
        {
            X = x; Y = y; Width = width; Height = height; TargetApp = targetApp ?? "";
        }

        public ZoneRect Clone() => new ZoneRect(X, Y, Width, Height, TargetApp);
    }

    /// <summary>
    /// 一个区域布局。
    /// Grid: RowPercents / ColumnPercents 为各行/列所占比例（和为 1），
    ///       CellMap[row][col] 为该单元格所属区域编号（0..ZoneCount-1），同一编号的单元格必须组成矩形。
    /// Canvas: CanvasZones 为比例坐标的矩形列表，列表顺序即 Z 序（后面的在上层）。
    /// </summary>
    public sealed class ZoneLayout
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "新布局";
        public LayoutKind Kind { get; set; } = LayoutKind.Grid;

        public List<double> RowPercents { get; set; } = new List<double>();
        public List<double> ColumnPercents { get; set; } = new List<double>();
        public List<List<int>> CellMap { get; set; } = new List<List<int>>();

        public List<ZoneRect> CanvasZones { get; set; } = new List<ZoneRect>();
        public List<string> ZoneApps { get; set; } = new List<string>();

        public int ZoneCount
        {
            get
            {
                if (Kind == LayoutKind.Canvas)
                {
                    return CanvasZones.Count;
                }
                int max = -1;
                foreach (var row in CellMap)
                {
                    foreach (int z in row)
                    {
                        if (z > max) max = z;
                    }
                }
                return max + 1;
            }
        }

        public string GetZoneApp(int zoneIndex)
        {
            if (zoneIndex < 0) return "";
            if (Kind == LayoutKind.Canvas && zoneIndex < CanvasZones.Count &&
                !string.IsNullOrWhiteSpace(CanvasZones[zoneIndex].TargetApp))
            {
                return CanvasZones[zoneIndex].TargetApp.Trim();
            }
            if (zoneIndex < ZoneApps.Count && !string.IsNullOrWhiteSpace(ZoneApps[zoneIndex]))
            {
                return ZoneApps[zoneIndex].Trim();
            }
            return "";
        }

        public void SetZoneApp(int zoneIndex, string? appName)
        {
            if (zoneIndex < 0) return;
            string clean = (appName ?? "").Trim();
            while (ZoneApps.Count <= zoneIndex)
            {
                ZoneApps.Add("");
            }
            ZoneApps[zoneIndex] = clean;
            if (Kind == LayoutKind.Canvas && zoneIndex < CanvasZones.Count)
            {
                CanvasZones[zoneIndex].TargetApp = clean;
            }
        }

        public void SyncZoneAppsCount()
        {
            int count = ZoneCount;
            if (Kind == LayoutKind.Canvas)
            {
                for (int i = 0; i < CanvasZones.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(CanvasZones[i].TargetApp) && i < ZoneApps.Count)
                    {
                        CanvasZones[i].TargetApp = ZoneApps[i] ?? "";
                    }
                }
                ZoneApps = CanvasZones.Select(z => z.TargetApp ?? "").ToList();
                return;
            }
            while (ZoneApps.Count < count)
            {
                ZoneApps.Add("");
            }
            if (ZoneApps.Count > count)
            {
                ZoneApps.RemoveRange(count, ZoneApps.Count - count);
            }
        }

        public ZoneLayout Clone()
        {
            var clone = new ZoneLayout
            {
                Id = Id,
                Name = Name,
                Kind = Kind,
                RowPercents = new List<double>(RowPercents),
                ColumnPercents = new List<double>(ColumnPercents),
                CellMap = CellMap.Select(r => new List<int>(r)).ToList(),
                CanvasZones = CanvasZones.Select(z => z.Clone()).ToList(),
                ZoneApps = new List<string>(ZoneApps)
            };
            clone.SyncZoneAppsCount();
            return clone;
        }

        /// <summary>返回各区域相对工作区的比例矩形 (0~1)，索引即区域编号。</summary>
        public List<RectangleF> GetZoneFractions()
        {
            var list = new List<RectangleF>();
            if (Kind == LayoutKind.Canvas)
            {
                foreach (var z in CanvasZones)
                {
                    list.Add(new RectangleF((float)z.X, (float)z.Y, (float)z.Width, (float)z.Height));
                }
                return list;
            }

            if (!IsGridValid())
            {
                return list;
            }

            double[] colStart = Prefix(ColumnPercents);
            double[] rowStart = Prefix(RowPercents);
            int count = ZoneCount;
            for (int z = 0; z < count; z++)
            {
                int minR = int.MaxValue, minC = int.MaxValue, maxR = -1, maxC = -1;
                for (int r = 0; r < CellMap.Count; r++)
                {
                    for (int c = 0; c < CellMap[r].Count; c++)
                    {
                        if (CellMap[r][c] != z) continue;
                        minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
                        minC = Math.Min(minC, c); maxC = Math.Max(maxC, c);
                    }
                }
                if (maxR < 0)
                {
                    list.Add(RectangleF.Empty);
                    continue;
                }
                double x = colStart[minC];
                double y = rowStart[minR];
                double w = colStart[maxC + 1] - x;
                double h = rowStart[maxR + 1] - y;
                list.Add(new RectangleF((float)x, (float)y, (float)w, (float)h));
            }
            return list;
        }

        /// <summary>
        /// 计算区域在给定工作区（物理像素）中的实际矩形，已应用相邻区域间距。
        /// 接触工作区边缘的区域边界保持贴边。
        /// </summary>
        public List<Rectangle> GetZoneRects(Rectangle workArea, int spacing)
        {
            spacing = Math.Max(0, spacing);
            int lowInset = spacing / 2;
            int highInset = spacing - lowInset;
            const double edgeTolerance = 0.000001;
            var result = new List<Rectangle>();
            foreach (var f in GetZoneFractions())
            {
                if (f.Width <= 0 || f.Height <= 0)
                {
                    result.Add(Rectangle.Empty);
                    continue;
                }

                int left = workArea.X + (int)Math.Round(f.X * workArea.Width);
                int top = workArea.Y + (int)Math.Round(f.Y * workArea.Height);
                int right = workArea.X + (int)Math.Round((f.X + f.Width) * workArea.Width);
                int bottom = workArea.Y + (int)Math.Round((f.Y + f.Height) * workArea.Height);

                if (Kind == LayoutKind.Canvas)
                {
                    // Canvas rectangles are the exact user-defined snap bounds. Keep negative
                    // and >1 ratios so zones can overlap and extend beyond the work area.
                    result.Add(Rectangle.FromLTRB(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom)));
                    continue;
                }

                // Keep zones flush with the work-area perimeter; apply spacing only on
                // interior boundaries so the editor outline and snapped window coincide.
                int insetLeft = f.X <= edgeTolerance ? 0 : lowInset;
                int insetTop = f.Y <= edgeTolerance ? 0 : lowInset;
                int insetRight = f.X + f.Width >= 1.0 - edgeTolerance ? 0 : highInset;
                int insetBottom = f.Y + f.Height >= 1.0 - edgeTolerance ? 0 : highInset;

                Rectangle raw = Rectangle.FromLTRB(left, top, right, bottom);
                Rectangle r = Rectangle.FromLTRB(
                    left + insetLeft,
                    top + insetTop,
                    right - insetRight,
                    bottom - insetBottom);
                if (r.Width < 1 || r.Height < 1)
                {
                    r = raw;
                }

                result.Add(Rectangle.Intersect(workArea, r));
            }
            return result;
        }

        public bool IsGridValid()
        {
            if (Kind != LayoutKind.Grid) return false;
            int rows = RowPercents.Count;
            int cols = ColumnPercents.Count;
            if (rows == 0 || cols == 0 || CellMap.Count != rows) return false;
            foreach (var row in CellMap)
            {
                if (row == null || row.Count != cols) return false;
                foreach (int z in row)
                {
                    if (z < 0) return false;
                }
            }
            if (RowPercents.Any(p => p <= 0) || ColumnPercents.Any(p => p <= 0)) return false;

            // 每个区域的单元格必须恰好构成其外接矩形
            int count = ZoneCount;
            for (int z = 0; z < count; z++)
            {
                int minR = int.MaxValue, minC = int.MaxValue, maxR = -1, maxC = -1, cells = 0;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (CellMap[r][c] != z) continue;
                        cells++;
                        minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
                        minC = Math.Min(minC, c); maxC = Math.Max(maxC, c);
                    }
                }
                if (cells == 0) return false;
                if (cells != (maxR - minR + 1) * (maxC - minC + 1)) return false;
            }
            return true;
        }

        /// <summary>将行/列比例归一化，使其和为 1。</summary>
        public void Normalize()
        {
            NormalizeList(RowPercents);
            NormalizeList(ColumnPercents);
        }

        private static void NormalizeList(List<double> list)
        {
            double sum = list.Sum();
            if (sum <= 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                list[i] /= sum;
            }
        }

        private static double[] Prefix(List<double> percents)
        {
            double sum = percents.Sum();
            if (sum <= 0) sum = 1;
            var arr = new double[percents.Count + 1];
            for (int i = 0; i < percents.Count; i++)
            {
                arr[i + 1] = arr[i] + percents[i] / sum;
            }
            arr[percents.Count] = 1.0;
            return arr;
        }
    }

    /// <summary>fancyzones.json 的根对象。</summary>
    public sealed class FancyZonesData
    {
        public int Version { get; set; } = 1;
        public string ActiveLayoutId { get; set; } = "";
        public List<ZoneLayout> Layouts { get; set; } = new List<ZoneLayout>();
    }
}
