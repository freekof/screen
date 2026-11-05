using System;
using System.Windows.Forms;

namespace ScreenCaptureTool.FancyZones
{
    /// <summary>
    /// 不受 DPI 变更自动缩放影响的 Form 基类。
    /// 用于全屏覆盖层、区域编辑器等必须保持像素精确尺寸的窗口。
    /// </summary>
    public class PixelExactForm : Form
    {
        public PixelExactForm()
        {
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override void WndProc(ref Message m)
        {
            // 拦截 WM_DPICHANGED 与 WM_GETDPISCALEDSIZE，阻止 WinForms 自动缩放。
            if (m.Msg == NativeMethods.WM_DPICHANGED ||
                m.Msg == NativeMethods.WM_GETDPISCALEDSIZE)
            {
                return;
            }
            base.WndProc(ref m);
        }

    }
}
