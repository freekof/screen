using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ScreenCaptureTool.FancyZones
{
    /// <summary>
    /// 半透明覆盖层，显示当前区域高亮。使用 UpdateLayeredWindow 实现真正的逐像素透明。
    /// </summary>
    public sealed class ZoneOverlayForm : PixelExactForm
    {
        private readonly List<Rectangle> zoneRects = new List<Rectangle>();
        private readonly HashSet<int> highlighted = new HashSet<int>();
        private bool isDirty = true;

        public ZoneOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT (click-through)
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        /// <summary>设置可用区域矩形列表（屏幕物理坐标）。</summary>
        public void SetZones(List<Rectangle> zones)
        {
            zoneRects.Clear();
            zoneRects.AddRange(zones);
            isDirty = true;
        }

        /// <summary>设置要高亮的区域索引。返回 true 表示与上次不同，已刷新。</summary>
        public bool SetHighlight(HashSet<int> indices)
        {
            if (indices.Count == highlighted.Count && indices.SetEquals(highlighted))
            {
                return false;
            }
            highlighted.Clear();
            foreach (int i in indices) highlighted.Add(i);
            isDirty = true;
            Render();
            return true;
        }

        /// <summary>显示覆盖层并绘制当前状态。</summary>
        public void ShowOverlay(Rectangle monitorBounds)
        {
            if (Bounds != monitorBounds)
            {
                this.Bounds = monitorBounds;
                isDirty = true;
            }
            if (!Visible)
            {
                Show();
                isDirty = true;
            }
            Render();
        }

        /// <summary>隐藏覆盖层。</summary>
        public void HideOverlay()
        {
            if (Visible)
            {
                Hide();
            }
            highlighted.Clear();
        }

        private void Render()
        {
            if (!isDirty || !IsHandleCreated || IsDisposed) return;
            isDirty = false;

            int w = Math.Max(1, Bounds.Width);
            int h = Math.Max(1, Bounds.Height);

            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(1, 0, 0, 0)); // 几乎全透明底

                    for (int i = 0; i < zoneRects.Count; i++)
                    {
                        Rectangle r = zoneRects[i];
                        // 转为覆盖层本地坐标
                        r.Offset(-Bounds.X, -Bounds.Y);

                        bool hl = highlighted.Contains(i);
                        Color fill = hl
                            ? Color.FromArgb(80, 60, 130, 255)
                            : Color.FromArgb(40, 200, 200, 200);
                        Color border = hl
                            ? Color.FromArgb(200, 60, 130, 255)
                            : Color.FromArgb(100, 180, 180, 180);

                        using (SolidBrush fb = new SolidBrush(fill))
                        {
                            g.FillRectangle(fb, r);
                        }
                        using (Pen pen = new Pen(border, 2))
                        {
                            g.DrawRectangle(pen, r);
                        }

                        // 区域编号
                        string label = (i + 1).ToString();
                        using (Font font = new Font("Segoe UI", 18, FontStyle.Bold))
                        using (SolidBrush tb = new SolidBrush(Color.FromArgb(hl ? 220 : 120, 255, 255, 255)))
                        using (StringFormat sf = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        })
                        {
                            g.DrawString(label, font, tb, r, sf);
                        }
                    }
                }

                ApplyLayeredBitmap(bmp);
            }
        }

        private void ApplyLayeredBitmap(Bitmap bmp)
        {
            IntPtr screenDC = IntPtr.Zero;
            IntPtr memDC = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                screenDC = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
                memDC = NativeMethods.CreateCompatibleDC(screenDC);
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memDC, hBitmap);

                var pptDst = new NativeMethods.POINT { X = Bounds.X, Y = Bounds.Y };
                var psize = new NativeMethods.SIZE { cx = bmp.Width, cy = bmp.Height };
                var pptSrc = new NativeMethods.POINT { X = 0, Y = 0 };
                var blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA
                };

                NativeMethods.UpdateLayeredWindow(Handle, IntPtr.Zero,
                    ref pptDst, ref psize, memDC, ref pptSrc,
                    0, ref blend, NativeMethods.ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero && memDC != IntPtr.Zero)
                    NativeMethods.SelectObject(memDC, oldBitmap);
                if (hBitmap != IntPtr.Zero)
                    NativeMethods.DeleteObject(hBitmap);
                if (memDC != IntPtr.Zero)
                    NativeMethods.DeleteDC(memDC);
                if (screenDC != IntPtr.Zero)
                    NativeMethods.DeleteDC(screenDC);
            }
        }
    }
}
