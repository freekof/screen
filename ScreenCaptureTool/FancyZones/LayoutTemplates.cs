using System;
using System.Collections.Generic;
using System.Linq;

namespace ScreenCaptureTool.FancyZones
{
    public enum LayoutTemplateType
    {
        Columns,
        Rows,
        Grid,
        PriorityGrid,
        Focus
    }

    public static class LayoutTemplates
    {
        public static string GetDisplayName(LayoutTemplateType t)
        {
            return t switch
            {
                LayoutTemplateType.Columns => "列",
                LayoutTemplateType.Rows => "行",
                LayoutTemplateType.Grid => "网格",
                LayoutTemplateType.PriorityGrid => "优先网格",
                LayoutTemplateType.Focus => "焦点",
                _ => t.ToString()
            };
        }

        public static ZoneLayout Create(LayoutTemplateType type, int zoneCount)
        {
            int maxZones = type == LayoutTemplateType.Focus ? 10 : 16;
            int n = Math.Clamp(zoneCount, 1, maxZones);
            string name = $"{GetDisplayName(type)} {n}";

            return type switch
            {
                LayoutTemplateType.Columns => CreateColumns(name, n),
                LayoutTemplateType.Rows => CreateRows(name, n),
                LayoutTemplateType.Grid => CreateGrid(name, n),
                LayoutTemplateType.PriorityGrid => CreatePriorityGrid(name, n),
                LayoutTemplateType.Focus => CreateFocus(name, n),
                _ => CreateColumns(name, n)
            };
        }

        public static List<ZoneLayout> CreateDefaults()
        {
            return new List<ZoneLayout>
            {
                Create(LayoutTemplateType.Columns, 2),
                Create(LayoutTemplateType.Columns, 3),
                Create(LayoutTemplateType.Rows, 3),
                Create(LayoutTemplateType.Grid, 4),
                Create(LayoutTemplateType.PriorityGrid, 3),
                Create(LayoutTemplateType.Focus, 3)
            };
        }

        private static ZoneLayout CreateColumns(string name, int n)
        {
            var layout = new ZoneLayout
            {
                Name = name,
                Kind = LayoutKind.Grid,
                RowPercents = new List<double> { 1.0 },
                ColumnPercents = EqualPercents(n),
                CellMap = new List<List<int>> { Enumerable.Range(0, n).ToList() }
            };
            layout.Normalize();
            return layout;
        }

        private static ZoneLayout CreateRows(string name, int n)
        {
            var layout = new ZoneLayout
            {
                Name = name,
                Kind = LayoutKind.Grid,
                RowPercents = EqualPercents(n),
                ColumnPercents = new List<double> { 1.0 },
                CellMap = Enumerable.Range(0, n).Select(i => new List<int> { i }).ToList()
            };
            layout.Normalize();
            return layout;
        }

        private static ZoneLayout CreateGrid(string name, int n)
        {
            int rows = Math.Max(1, (int)Math.Sqrt(n));
            int cols = (int)Math.Ceiling(n / (double)rows);
            int lastCount = n - (rows - 1) * cols;

            int totalCols = lastCount == cols ? cols : cols * lastCount;
            int normalSpan = lastCount == cols ? 1 : lastCount;
            int lastSpan = lastCount == cols ? 1 : cols;

            var cellMap = new List<List<int>>(rows);
            for (int r = 0; r < rows - 1; r++)
            {
                var row = new List<int>(totalCols);
                for (int c = 0; c < cols; c++)
                {
                    int zoneIdx = r * cols + c;
                    for (int s = 0; s < normalSpan; s++)
                    {
                        row.Add(zoneIdx);
                    }
                }
                cellMap.Add(row);
            }

            var lastRow = new List<int>(totalCols);
            for (int c = 0; c < lastCount; c++)
            {
                int zoneIdx = (rows - 1) * cols + c;
                for (int s = 0; s < lastSpan; s++)
                {
                    lastRow.Add(zoneIdx);
                }
            }
            cellMap.Add(lastRow);

            var layout = new ZoneLayout
            {
                Name = name,
                Kind = LayoutKind.Grid,
                RowPercents = EqualPercents(rows),
                ColumnPercents = EqualPercents(totalCols),
                CellMap = cellMap
            };
            layout.Normalize();
            return layout;
        }

        private static ZoneLayout CreatePriorityGrid(string name, int n)
        {
            if (n == 1)
            {
                return CreateColumns(name, 1);
            }
            if (n == 2)
            {
                return CreateColumns(name, 2);
            }
            if (n == 3)
            {
                var layout3 = new ZoneLayout
                {
                    Name = name,
                    Kind = LayoutKind.Grid,
                    RowPercents = new List<double> { 1.0 },
                    ColumnPercents = new List<double> { 0.25, 0.5, 0.25 },
                    CellMap = new List<List<int>> { new List<int> { 0, 1, 2 } }
                };
                layout3.Normalize();
                return layout3;
            }

            int leftCount = (n - 1) / 2;
            int rightCount = (n - 1) - leftCount;
            int totalRows = Lcm(leftCount, rightCount);
            int leftSpan = totalRows / leftCount;
            int rightSpan = totalRows / rightCount;
            int middleZone = leftCount;

            var cellMap = new List<List<int>>(totalRows);
            for (int r = 0; r < totalRows; r++)
            {
                int leftZone = r / leftSpan;
                int rightZone = leftCount + 1 + (r / rightSpan);
                cellMap.Add(new List<int> { leftZone, middleZone, rightZone });
            }

            var layout = new ZoneLayout
            {
                Name = name,
                Kind = LayoutKind.Grid,
                RowPercents = EqualPercents(totalRows),
                ColumnPercents = new List<double> { 0.25, 0.5, 0.25 },
                CellMap = cellMap
            };
            layout.Normalize();
            return layout;
        }

        private static ZoneLayout CreateFocus(string name, int n)
        {
            var zones = new List<ZoneRect>(n);
            const double w = 0.5;
            const double h = 0.5;
            for (int i = 0; i < n; i++)
            {
                double x = Math.Clamp(0.1 + i * 0.05, 0.0, 1.0 - w);
                double y = Math.Clamp(0.1 + i * 0.05, 0.0, 1.0 - h);
                zones.Add(new ZoneRect(x, y, w, h));
            }

            return new ZoneLayout
            {
                Name = name,
                Kind = LayoutKind.Canvas,
                CanvasZones = zones
            };
        }

        private static List<double> EqualPercents(int count)
        {
            var list = new List<double>(count);
            double val = 1.0 / count;
            double sum = 0.0;
            for (int i = 0; i < count - 1; i++)
            {
                list.Add(val);
                sum += val;
            }
            list.Add(Math.Max(1e-9, 1.0 - sum));
            return list;
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = b;
                b = a % b;
                a = t;
            }
            return a;
        }

        private static int Lcm(int a, int b)
        {
            if (a <= 0 || b <= 0) return 1;
            return (a / Gcd(a, b)) * b;
        }
    }
}
