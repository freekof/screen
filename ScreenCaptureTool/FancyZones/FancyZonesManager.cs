using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenCaptureTool.FancyZones
{
    /// <summary>
    /// FancyZones 主管理器：拖动检测 → 区域高亮 → 窗口吸附。
    /// 在 UI 线程创建、运行。
    /// </summary>
    public sealed class FancyZonesManager : IDisposable
    {
        private readonly Settings settings;
        private readonly ZoneStore store;
        private ZoneOverlayForm? overlay;

        // WinEvent 钩子（必须保持引用以防 GC）
        private NativeMethods.WinEventDelegate? winEventProc;
        private IntPtr hookStart;
        private IntPtr hookEnd;
        private NativeMethods.LowLevelMouseProc? mouseHookProc;
        private IntPtr mouseHook;

        // 拖动状态
        private IntPtr dragHwnd;
        private Rectangle dragStartRect;
        private bool dragActive;
        private bool overlayShowing;
        private bool startWasMaximized;
        private bool resizeDetected;
        private bool rightButtonDown;
        private bool suppressRightButtonRelease;
        private int anchorZone = -1; // Ctrl 多选锚点
        private Screen? overlayScreen;
        private System.Windows.Forms.Timer? pollTimer;

        // 吸附记忆
        private readonly Dictionary<IntPtr, Rectangle> originalSizes = new Dictionary<IntPtr, Rectangle>();
        private readonly uint currentPid;

        public FancyZonesManager(Settings settings, ZoneStore store)
        {
            this.settings = settings;
            this.store = store;
            using (var proc = Process.GetCurrentProcess())
            {
                currentPid = (uint)proc.Id;
            }
        }

        // ── 启动 / 停止 ──

        public void Start()
        {
            if (hookStart != IntPtr.Zero) return;

            winEventProc = OnWinEvent;
            hookStart = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_MOVESIZESTART,
                NativeMethods.EVENT_SYSTEM_MOVESIZESTART,
                IntPtr.Zero, winEventProc, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            hookEnd = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
                NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero, winEventProc, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            mouseHookProc = OnLowLevelMouseEvent;
            mouseHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL, mouseHookProc, IntPtr.Zero, 0);
            if (mouseHook == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("Unable to install the FancyZones right-button hook.");
            }
        }

        public void Stop()
        {
            StopPolling();
            dragActive = false;
            HideOverlay();
            if (hookStart != IntPtr.Zero) { NativeMethods.UnhookWinEvent(hookStart); hookStart = IntPtr.Zero; }
            if (hookEnd != IntPtr.Zero) { NativeMethods.UnhookWinEvent(hookEnd); hookEnd = IntPtr.Zero; }
            if (mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            mouseHookProc = null;
            rightButtonDown = false;
            suppressRightButtonRelease = false;
        }

        public void Dispose()
        {
            Stop();
            overlay?.Close();
            overlay?.Dispose();
            overlay = null;
        }

        // ── WinEvent 回调 ──

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != 0 || hwnd == IntPtr.Zero) return;

            if (eventType == NativeMethods.EVENT_SYSTEM_MOVESIZESTART)
            {
                OnMoveSizeStart(hwnd);
            }
            else if (eventType == NativeMethods.EVENT_SYSTEM_MOVESIZEEND)
            {
                OnMoveSizeEnd(hwnd);
            }
        }

        private IntPtr OnLowLevelMouseEvent(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= NativeMethods.HC_ACTION)
            {
                int message = wParam.ToInt32();
                if (message == NativeMethods.WM_RBUTTONDOWN || message == NativeMethods.WM_RBUTTONDBLCLK)
                {
                    rightButtonDown = true;
                    if (dragActive)
                    {
                        suppressRightButtonRelease = true;
                        return new IntPtr(1);
                    }
                }
                else if (message == NativeMethods.WM_RBUTTONUP)
                {
                    bool suppress = dragActive || suppressRightButtonRelease;
                    rightButtonDown = false;
                    suppressRightButtonRelease = false;
                    if (suppress)
                    {
                        return new IntPtr(1);
                    }
                }
            }

            return NativeMethods.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        private void OnMoveSizeStart(IntPtr hwnd)
        {
            if (!settings.FancyZonesEnabled) return;
            if (!IsEligibleWindow(hwnd)) return;

            // 判断是 Move 还是 Resize（使用 WM_NCHITTEST）
            NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
            IntPtr lParam = (IntPtr)((pt.Y << 16) | (pt.X & 0xFFFF));
            NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WM_NCHITTEST,
                IntPtr.Zero, lParam, NativeMethods.SMTO_ABORTIFHUNG, 100, out IntPtr result);
            int ht = result.ToInt32();
            // 如果光标在窗口边缘（Resize），不启动区域吸附
            if (NativeMethods.IsSizeHitTest(ht)) return;

            rightButtonDown = NativeMethods.IsKeyDown(NativeMethods.VK_RBUTTON);

            NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT wr);
            Rectangle currentRect = wr.ToRectangle();
            if (settings.FancyZonesRestoreSize && originalSizes.TryGetValue(hwnd, out Rectangle originalSize))
            {
                NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
                double relativeX = currentRect.Width > 0
                    ? Math.Clamp((cursor.X - currentRect.Left) / (double)currentRect.Width, 0.0, 1.0)
                    : 0.5;
                double relativeY = currentRect.Height > 0
                    ? Math.Clamp((cursor.Y - currentRect.Top) / (double)currentRect.Height, 0.0, 1.0)
                    : 0.5;
                int restoredX = cursor.X - (int)Math.Round(relativeX * originalSize.Width);
                int restoredY = cursor.Y - (int)Math.Round(relativeY * originalSize.Height);
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                    restoredX, restoredY, originalSize.Width, originalSize.Height,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                NativeMethods.GetWindowRect(hwnd, out wr);
            }

            dragHwnd = hwnd;
            dragStartRect = wr.ToRectangle();
            dragActive = true;
            startWasMaximized = NativeMethods.IsZoomed(hwnd);
            resizeDetected = false;
            anchorZone = -1;
            overlayShowing = false;
            overlayScreen = null;

            StartPolling();
        }

        private void OnMoveSizeEnd(IntPtr hwnd)
        {
            if (!dragActive || hwnd != dragHwnd) return;
            dragActive = false;
            StopPolling();

            try
            {
                if (resizeDetected)
                {
                    HideOverlay();
                    return;
                }

                if (!overlayShowing)
                {
                    HideOverlay();
                    RestoreOriginalSize(hwnd);
                    return;
                }

                if (!IsActivationChordDown())
                {
                    HideOverlay();
                    RestoreOriginalSize(hwnd);
                    return;
                }

                // 获取选中的区域
                var selected = GetCurrentHighlight();
                if (selected.Count == 0)
                {
                    HideOverlay();
                    // 拖到区域外松开 → 恢复之前记录的未吸附尺寸。
                    RestoreOriginalSize(hwnd);
                    return;
                }

                // 计算合并区域
                ZoneLayout? layout = store.ActiveLayout;
                if (layout == null) { HideOverlay(); return; }

                Screen screen = overlayScreen ?? Screen.PrimaryScreen!;
                var zones = layout.GetZoneRects(screen.WorkingArea, settings.FancyZonesSpacing);
                Rectangle target = Rectangle.Empty;
                foreach (int idx in selected)
                {
                    if (idx >= 0 && idx < zones.Count)
                    {
                        target = target.IsEmpty ? zones[idx] : Rectangle.Union(target, zones[idx]);
                    }
                }

                HideOverlay();

                if (target.IsEmpty) return;

                SnapWindow(hwnd, target);
            }
            finally
            {
                HideOverlay();
            }
        }

        // ── 轮询 ──

        private void StartPolling()
        {
            if (pollTimer != null) return;
            pollTimer = new System.Windows.Forms.Timer { Interval = 16 };
            pollTimer.Tick += PollTick;
            pollTimer.Start();
        }

        private void StopPolling()
        {
            if (pollTimer == null) return;
            pollTimer.Stop();
            pollTimer.Tick -= PollTick;
            pollTimer.Dispose();
            pollTimer = null;
        }

        private void PollTick(object? sender, EventArgs e)
        {
            if (!dragActive || dragHwnd == IntPtr.Zero)
            {
                StopPolling();
                HideOverlay();
                return;
            }

            // 检查窗口是否仍在
            if (!NativeMethods.IsWindow(dragHwnd))
            {
                dragActive = false;
                StopPolling();
                HideOverlay();
                return;
            }

            // 如果窗口尺寸变了（真正 Resize，不是 Move），取消
            NativeMethods.GetWindowRect(dragHwnd, out NativeMethods.RECT nowRect);
            Rectangle now = nowRect.ToRectangle();
            // 允许位置变化，但尺寸不应该变化超过 10px（考虑从最大化还原的情况）
            bool sizeChanged = Math.Abs(now.Width - dragStartRect.Width) > 10 ||
                               Math.Abs(now.Height - dragStartRect.Height) > 10;
            // Windows begins a move from a maximized window by restoring it; treat that one
            // expected size transition as the new baseline, while rejecting real resize drags.
            if (sizeChanged && startWasMaximized && !NativeMethods.IsZoomed(dragHwnd))
            {
                dragStartRect = now;
                startWasMaximized = false;
            }
            else if (sizeChanged)
            {
                resizeDetected = true;
                HideOverlay();
                return;
            }

            // 检查激活键
            bool activatorDown = IsActivationChordDown();
            if (!activatorDown)
            {
                if (overlayShowing) HideOverlay();
                overlayShowing = false;
                anchorZone = -1;
                return;
            }

            // ESC 取消
            if (NativeMethods.IsKeyDown(NativeMethods.VK_ESCAPE))
            {
                dragActive = false;
                StopPolling();
                HideOverlay();
                return;
            }

            NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
            Point cursorPt = new Point(cursor.X, cursor.Y);

            // 确定光标所在的显示器
            Screen curScreen = Screen.FromPoint(cursorPt);
            ZoneLayout? layout = store.ActiveLayout;
            if (layout == null)
            {
                if (overlayShowing) HideOverlay();
                return;
            }

            var zones = layout.GetZoneRects(curScreen.WorkingArea, settings.FancyZonesSpacing);

            // 找光标命中的区域（选面积最小的）
            int hitZone = -1;
            int hitArea = int.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].Contains(cursorPt) && zones[i].Width * zones[i].Height <= hitArea)
                {
                    hitZone = i;
                    hitArea = zones[i].Width * zones[i].Height;
                }
            }

            // 多区域选择（Ctrl 键）
            bool ctrlDown = IsMultiSelectDown();
            var hlSet = new HashSet<int>();

            if (ctrlDown && hitZone >= 0)
            {
                if (anchorZone < 0) anchorZone = hitZone;
                // 锚点与当前区域之间所有区域
                if (anchorZone >= 0 && anchorZone < zones.Count && hitZone < zones.Count)
                {
                    Rectangle union = Rectangle.Union(zones[anchorZone], zones[hitZone]);
                    for (int i = 0; i < zones.Count; i++)
                    {
                        if (zones[i].IntersectsWith(union)) hlSet.Add(i);
                    }
                }
            }
            else
            {
                anchorZone = -1;
                if (hitZone >= 0) hlSet.Add(hitZone);
            }

            // 显示/更新覆盖层
            EnsureOverlay();
            if (overlayScreen == null || overlayScreen.DeviceName != curScreen.DeviceName)
            {
                overlay!.SetZones(zones);
                overlayScreen = curScreen;
            }
            overlay!.ShowOverlay(curScreen.Bounds);
            overlay.SetHighlight(hlSet);
            overlayShowing = true;
        }

        private HashSet<int> GetCurrentHighlight()
        {
            if (overlay == null) return new HashSet<int>();
            // 重新计算一次当前高亮（与上次 PollTick 一致）
            NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
            Point cursorPt = new Point(cursor.X, cursor.Y);
            Screen curScreen = Screen.FromPoint(cursorPt);
            overlayScreen = curScreen;
            ZoneLayout? layout = store.ActiveLayout;
            if (layout == null) return new HashSet<int>();

            var zones = layout.GetZoneRects(curScreen.WorkingArea, settings.FancyZonesSpacing);
            int hitZone = -1;
            int hitArea = int.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].Contains(cursorPt) && zones[i].Width * zones[i].Height <= hitArea)
                {
                    hitZone = i;
                    hitArea = zones[i].Width * zones[i].Height;
                }
            }

            bool ctrlDown = IsMultiSelectDown();
            var hlSet = new HashSet<int>();
            if (ctrlDown && anchorZone >= 0 && hitZone >= 0 &&
                anchorZone < zones.Count && hitZone < zones.Count)
            {
                Rectangle union = Rectangle.Union(zones[anchorZone], zones[hitZone]);
                for (int i = 0; i < zones.Count; i++)
                {
                    if (zones[i].IntersectsWith(union)) hlSet.Add(i);
                }
            }
            else if (hitZone >= 0)
            {
                hlSet.Add(hitZone);
            }
            return hlSet;
        }

        // ── 吸附 ──

        private void SnapWindow(IntPtr hwnd, Rectangle target)
        {
            if (!NativeMethods.IsWindow(hwnd)) return;

            if (!originalSizes.ContainsKey(hwnd) && NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT originalRect))
            {
                originalSizes[hwnd] = originalRect.ToRectangle();
            }

            // 还原最大化/最小化
            if (NativeMethods.IsZoomed(hwnd) || NativeMethods.IsIconic(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            }

            // 不可见边框补偿
            NativeMethods.GetInvisibleBorders(hwnd, out int bLeft, out int bTop, out int bRight, out int bBottom);
            Rectangle adjusted = new Rectangle(
                target.X - bLeft,
                target.Y - bTop,
                target.Width + bLeft + bRight,
                target.Height + bTop + bBottom);

            // 设置两次以应对跨 DPI 显示器的 WM_DPICHANGED 干扰
            uint flags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE;
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                adjusted.X, adjusted.Y, adjusted.Width, adjusted.Height, flags);
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                adjusted.X, adjusted.Y, adjusted.Width, adjusted.Height, flags);
        }

        private void RestoreOriginalSize(IntPtr hwnd)
        {
            if (!settings.FancyZonesRestoreSize || !originalSizes.TryGetValue(hwnd, out Rectangle original))
            {
                return;
            }

            if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT current))
            {
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                    current.Left, current.Top, original.Width, original.Height,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }
            originalSizes.Remove(hwnd);
        }

        // ── 覆盖层 ──

        private void EnsureOverlay()
        {
            if (overlay == null || overlay.IsDisposed)
            {
                overlay = new ZoneOverlayForm();
            }
        }

        private void HideOverlay()
        {
            overlay?.HideOverlay();
            overlayShowing = false;
        }

        // ── 窗口过滤 ──

        private bool IsEligibleWindow(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
                return false;

            // 必须是顶层窗口
            if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
                return false;

            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

            // 需要粗边框（可调整大小）
            if ((style & NativeMethods.WS_THICKFRAME) == 0) return false;
            // 排除子窗口
            if ((style & NativeMethods.WS_CHILD) != 0) return false;
            // 排除工具窗口
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return false;

            // 排除自身进程
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == currentPid) return false;

            // 排除排除列表
            if (!string.IsNullOrWhiteSpace(settings.FancyZonesExcludedApps))
            {
                try
                {
                    var proc = Process.GetProcessById((int)pid);
                    string name = proc.ProcessName;
                    string[] excluded = settings.FancyZonesExcludedApps
                        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string ex in excluded)
                    {
                        if (name.Equals(ex.Trim(), StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                }
                catch
                {
                    // 访问被拒绝（提升窗口等），仍然尝试
                }
            }

            return true;
        }

        // ── 按键检测 ──

        private bool IsActivatorDown()
        {
            int vk = settings.FancyZonesActivationKey switch
            {
                0 => NativeMethods.VK_SHIFT,
                1 => NativeMethods.VK_CONTROL,
                2 => NativeMethods.VK_MENU,
                _ => NativeMethods.VK_SHIFT
            };
            return NativeMethods.IsKeyDown(vk);
        }

        private bool IsActivationChordDown()
        {
            bool rightDown = mouseHook != IntPtr.Zero
                ? rightButtonDown
                : NativeMethods.IsKeyDown(NativeMethods.VK_RBUTTON);
            return IsActivatorDown() && rightDown;
        }

        private bool IsMultiSelectDown()
        {
            // 多选键 = Ctrl；如果激活键已经是 Ctrl，则用 Shift
            int activatorVk = settings.FancyZonesActivationKey switch
            {
                0 => NativeMethods.VK_SHIFT,
                1 => NativeMethods.VK_CONTROL,
                2 => NativeMethods.VK_MENU,
                _ => NativeMethods.VK_SHIFT
            };
            int multiVk = activatorVk == NativeMethods.VK_CONTROL
                ? NativeMethods.VK_SHIFT
                : NativeMethods.VK_CONTROL;
            return NativeMethods.IsKeyDown(multiVk);
        }
    }
}
