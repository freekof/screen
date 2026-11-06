using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
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
        private bool isCustomDrag;
        private bool overlayShowing;
        private bool startWasMaximized;
        private bool resizeDetected;
        private bool rightButtonDown;
        private bool suppressRightButtonRelease;
        private int anchorZone = -1; // Ctrl 多选锚点
        private int lastLoggedHitZone = -2;
        private Screen? overlayScreen;
        private System.Windows.Forms.Timer? pollTimer;

        // 自定义拖动（如雷电模拟器等自绘无边框窗口或高权限窗口）候选检测
        private bool wasLeftButtonDown;
        private IntPtr candidateHwnd;
        private Rectangle candidateStartRect;
        private bool candidateLogged;

        // 吸附记忆
        private readonly Dictionary<IntPtr, Rectangle> originalSizes = new Dictionary<IntPtr, Rectangle>();
        private readonly uint currentPid;
        private bool isDisposed;

        private static string DebugLogPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fancyzones_debug.log");

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
            if (isDisposed || hookStart != IntPtr.Zero) return;

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
                NativeMethods.WH_MOUSE_LL, mouseHookProc,
                NativeMethods.GetModuleHandle(null), 0);
            if (mouseHook == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"Unable to install the FancyZones right-button hook (Win32 error {err}).");
                WriteDebugLog($"[WARN] WH_MOUSE_LL install failed, Win32Error={err}");
            }

            StartPolling();
            WriteDebugLog($"[START] FancyZones started. PID={currentPid}, IsAdmin={IsRunningAsAdmin()}, hookStart=0x{hookStart:X}, hookEnd=0x{hookEnd:X}, mouseHook=0x{mouseHook:X}");
        }

        public void Stop()
        {
            StopPolling();
            dragActive = false;
            isCustomDrag = false;
            candidateHwnd = IntPtr.Zero;
            wasLeftButtonDown = false;
            HideOverlay();
            if (hookStart != IntPtr.Zero) { NativeMethods.UnhookWinEvent(hookStart); hookStart = IntPtr.Zero; }
            if (hookEnd != IntPtr.Zero) { NativeMethods.UnhookWinEvent(hookEnd); hookEnd = IntPtr.Zero; }
            if (mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            mouseHookProc = null;
            rightButtonDown = false;
            suppressRightButtonRelease = false;
            WriteDebugLog("[STOP] FancyZones stopped.");
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            Stop();
            var ov = overlay;
            overlay = null;
            if (ov != null && !ov.IsDisposed)
            {
                try
                {
                    ov.Dispose();
                }
                catch
                {
                    // Ignore disposal errors during application shutdown.
                }
            }
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
                OnMoveSizeEnd(hwnd, "EVENT_SYSTEM_MOVESIZEEND");
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
                        WriteDebugLog($"[MOUSE_HOOK] Swallowed RBUTTONDOWN during active drag, hwnd=0x{dragHwnd:X}");
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
                        WriteDebugLog($"[MOUSE_HOOK] Swallowed RBUTTONUP during active drag, hwnd=0x{dragHwnd:X}");
                        return new IntPtr(1);
                    }
                }
            }

            return NativeMethods.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        private void OnMoveSizeStart(IntPtr hwnd)
        {
            if (!settings.FancyZonesEnabled) return;

            IntPtr rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (rootHwnd != IntPtr.Zero)
            {
                hwnd = rootHwnd;
            }

            if (!IsEligibleWindow(hwnd, out string rejectReason))
            {
                WriteDebugLog($"[MOVESIZESTART_REJECT] {DescribeWindow(hwnd)} Reason={rejectReason}");
                return;
            }

            // 判断是 Move 还是 Resize（使用 WM_NCHITTEST）
            NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
            IntPtr lParam = (IntPtr)((pt.Y << 16) | (pt.X & 0xFFFF));
            NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WM_NCHITTEST,
                IntPtr.Zero, lParam, NativeMethods.SMTO_ABORTIFHUNG, 100, out IntPtr result);
            int ht = result.ToInt32();
            if (NativeMethods.IsSizeHitTest(ht))
            {
                WriteDebugLog($"[MOVESIZESTART_RESIZE_SKIP] {DescribeWindow(hwnd)} hitTest={ht}");
                return;
            }

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
                currentRect = wr.ToRectangle();
            }

            BeginDragTracking(hwnd, currentRect, customDrag: false, hitTest: ht);
        }

        private void BeginDragTracking(IntPtr hwnd, Rectangle startRect, bool customDrag, int hitTest = 0)
        {
            dragHwnd = hwnd;
            dragStartRect = startRect;
            dragActive = true;
            isCustomDrag = customDrag;
            startWasMaximized = NativeMethods.IsZoomed(hwnd);
            resizeDetected = false;
            anchorZone = -1;
            lastLoggedHitZone = -2;
            overlayShowing = false;
            overlayScreen = null;

            string mode = customDrag ? "CUSTOM_DRAG_START" : "MOVESIZESTART";
            WriteDebugLog($"[{mode}] {DescribeWindow(hwnd)} startRect={startRect} hitTest={hitTest}");
        }

        private void OnMoveSizeEnd(IntPtr hwnd, string source)
        {
            if (!dragActive || hwnd != dragHwnd) return;
            bool wasOverlayShowing = overlayShowing;
            bool wasResizeDetected = resizeDetected;
            dragActive = false;
            isCustomDrag = false;
            candidateHwnd = IntPtr.Zero;

            try
            {
                if (wasResizeDetected)
                {
                    WriteDebugLog($"[DRAG_END_RESIZE] source={source} hwnd=0x{hwnd:X}");
                    HideOverlay();
                    return;
                }

                if (!wasOverlayShowing)
                {
                    WriteDebugLog($"[DRAG_END_NO_OVERLAY] source={source} hwnd=0x{hwnd:X}");
                    HideOverlay();
                    RestoreOriginalSize(hwnd);
                    return;
                }

                if (!IsActivationChordDown())
                {
                    WriteDebugLog($"[DRAG_END_CHORD_RELEASED] source={source} hwnd=0x{hwnd:X}");
                    HideOverlay();
                    RestoreOriginalSize(hwnd);
                    return;
                }

                // 获取选中的区域
                var selected = GetCurrentHighlight();
                if (selected.Count == 0)
                {
                    WriteDebugLog($"[DRAG_END_OUTSIDE_ZONE] source={source} hwnd=0x{hwnd:X}");
                    HideOverlay();
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

                WriteDebugLog($"[DRAG_END_SNAP] source={source} hwnd=0x{hwnd:X} zones=[{string.Join(",", selected)}] target={target}");
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
            if (!settings.FancyZonesEnabled)
            {
                if (overlayShowing) HideOverlay();
                return;
            }

            bool leftDown = NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON);

            // 1. 检测自绘窗口（如雷电模拟器等不触发 EVENT_SYSTEM_MOVESIZESTART 的窗口）或高权限窗口拖动
            if (leftDown && !wasLeftButtonDown)
            {
                wasLeftButtonDown = true;
                if (!dragActive)
                {
                    IntPtr fg = NativeMethods.GetForegroundWindow();
                    if (fg != IntPtr.Zero)
                    {
                        IntPtr root = NativeMethods.GetAncestor(fg, NativeMethods.GA_ROOT);
                        if (root != IntPtr.Zero) fg = root;
                    }
                    if (fg != IntPtr.Zero && NativeMethods.GetWindowRect(fg, out NativeMethods.RECT r))
                    {
                        candidateHwnd = fg;
                        candidateStartRect = r.ToRectangle();
                        candidateLogged = false;
                    }
                }
            }
            else if (!leftDown && wasLeftButtonDown)
            {
                wasLeftButtonDown = false;
                candidateHwnd = IntPtr.Zero;
                candidateLogged = false;
                if (dragActive)
                {
                    // 左键已松开：无论是自定义拖动还是系统拖动未及时收到 MOVESIZEEND，立即完成吸附
                    OnMoveSizeEnd(dragHwnd, isCustomDrag ? "LBUTTONUP_CUSTOM" : "LBUTTONUP_FALLBACK");
                    return;
                }
            }

            if (!dragActive && leftDown && candidateHwnd != IntPtr.Zero && NativeMethods.IsWindow(candidateHwnd))
            {
                if (NativeMethods.GetWindowRect(candidateHwnd, out NativeMethods.RECT curR))
                {
                    Rectangle cur = curR.ToRectangle();
                    bool moved = Math.Abs(cur.Left - candidateStartRect.Left) >= 3 ||
                                 Math.Abs(cur.Top - candidateStartRect.Top) >= 3;
                    bool sameSize = Math.Abs(cur.Width - candidateStartRect.Width) <= 10 &&
                                    Math.Abs(cur.Height - candidateStartRect.Height) <= 10;

                    if (moved && sameSize)
                    {
                        if (IsEligibleWindow(candidateHwnd, out string reason))
                        {
                            BeginDragTracking(candidateHwnd, candidateStartRect, customDrag: true);
                        }
                        else if (!candidateLogged)
                        {
                            candidateLogged = true;
                            WriteDebugLog($"[CUSTOM_DRAG_REJECT] {DescribeWindow(candidateHwnd)} Reason={reason}");
                        }
                    }
                    else if (!candidateLogged && IsActivationChordDown())
                    {
                        candidateLogged = true;
                        bool eligible = IsEligibleWindow(candidateHwnd, out string reason);
                        WriteDebugLog($"[CHORD_WITHOUT_MOVESIZESTART] {DescribeWindow(candidateHwnd)} moved={moved} sameSize={sameSize} eligible={eligible} reason={reason}");
                        if (eligible && sameSize)
                        {
                            BeginDragTracking(candidateHwnd, candidateStartRect, customDrag: true);
                        }
                    }
                }
            }

            if (!dragActive || dragHwnd == IntPtr.Zero)
            {
                if (overlayShowing) HideOverlay();
                return;
            }

            // 检查窗口是否仍在
            if (!NativeMethods.IsWindow(dragHwnd))
            {
                dragActive = false;
                isCustomDrag = false;
                HideOverlay();
                return;
            }

            // 如果窗口尺寸变了（真正 Resize，不是 Move），取消
            NativeMethods.GetWindowRect(dragHwnd, out NativeMethods.RECT nowRect);
            Rectangle now = nowRect.ToRectangle();
            bool sizeChanged = Math.Abs(now.Width - dragStartRect.Width) > 12 ||
                               Math.Abs(now.Height - dragStartRect.Height) > 12;
            // Windows begins a move from a maximized window by restoring it; treat that one
            // expected size transition as the new baseline, while rejecting real resize drags.
            if (sizeChanged && startWasMaximized && !NativeMethods.IsZoomed(dragHwnd))
            {
                dragStartRect = now;
                startWasMaximized = false;
            }
            else if (sizeChanged && !overlayShowing)
            {
                resizeDetected = true;
                WriteDebugLog($"[RESIZE_DETECTED] {DescribeWindow(dragHwnd)} startRect={dragStartRect} nowRect={now}");
                HideOverlay();
                return;
            }

            // 检查激活键（Shift + 右键）
            bool activatorDown = IsActivationChordDown();
            if (!activatorDown)
            {
                if (overlayShowing) HideOverlay();
                overlayShowing = false;
                anchorZone = -1;
                lastLoggedHitZone = -2;
                return;
            }

            // ESC 取消
            if (NativeMethods.IsKeyDown(NativeMethods.VK_ESCAPE))
            {
                WriteDebugLog($"[DRAG_ESC_CANCEL] hwnd=0x{dragHwnd:X}");
                dragActive = false;
                isCustomDrag = false;
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

            if (hitZone != lastLoggedHitZone)
            {
                lastLoggedHitZone = hitZone;
                WriteDebugLog($"[ZONE_HOVER] hwnd=0x{dragHwnd:X} cursor={cursorPt} screen={curScreen.DeviceName} hitZone={hitZone}");
            }

            // 多区域选择（Ctrl 键）
            bool ctrlDown = IsMultiSelectDown();
            var hlSet = new HashSet<int>();

            if (ctrlDown && hitZone >= 0)
            {
                if (anchorZone < 0) anchorZone = hitZone;
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
            bool ok1 = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                adjusted.X, adjusted.Y, adjusted.Width, adjusted.Height, flags);
            int err1 = ok1 ? 0 : Marshal.GetLastWin32Error();
            bool ok2 = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
                adjusted.X, adjusted.Y, adjusted.Width, adjusted.Height, flags);
            int err2 = ok2 ? 0 : Marshal.GetLastWin32Error();

            NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT afterRect);
            string adminHint = (!ok1 || !ok2) && (err1 == 5 || err2 == 5)
                ? " [ERROR_ACCESS_DENIED(5): 目标窗口以管理员权限运行，请以管理员身份运行抓屏软件]"
                : "";
            WriteDebugLog($"[SNAP_RESULT] {DescribeWindow(hwnd)} target={target} adjusted={adjusted} ok1={ok1}(err={err1}) ok2={ok2}(err={err2}) afterRect={afterRect.ToRectangle()}{adminHint}");
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

        private bool IsEligibleWindow(IntPtr hwnd, out string rejectReason)
        {
            rejectReason = "";
            if (!NativeMethods.IsWindow(hwnd))
            {
                rejectReason = "NotWindow";
                return false;
            }
            if (!NativeMethods.IsWindowVisible(hwnd))
            {
                rejectReason = "NotVisible";
                return false;
            }
            if (NativeMethods.IsIconic(hwnd))
            {
                rejectReason = "Iconic";
                return false;
            }

            // 必须是顶层窗口
            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root != IntPtr.Zero && root != hwnd)
            {
                rejectReason = $"NotRoot(root=0x{root:X})";
                return false;
            }

            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

            // 排除子窗口或禁用窗口
            if ((style & NativeMethods.WS_CHILD) != 0)
            {
                rejectReason = "WS_CHILD";
                return false;
            }
            if ((style & NativeMethods.WS_DISABLED) != 0)
            {
                rejectReason = "WS_DISABLED";
                return false;
            }
            // 排除工具窗口和不可激活浮层
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
            {
                rejectReason = "WS_EX_TOOLWINDOW";
                return false;
            }
            if ((exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
            {
                rejectReason = "WS_EX_NOACTIVATE";
                return false;
            }

            // 排除桌面与任务栏等系统外壳窗口
            string cls = NativeMethods.GetWindowClass(hwnd);
            if (cls.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
            {
                rejectReason = $"SystemShellClass({cls})";
                return false;
            }

            if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
            {
                if (rect.Width < 80 || rect.Height < 80)
                {
                    rejectReason = $"TooSmall({rect.Width}x{rect.Height})";
                    return false;
                }
            }

            // 排除自身进程
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == currentPid)
            {
                rejectReason = "OwnProcess";
                return false;
            }

            // 排除排除列表
            if (!string.IsNullOrWhiteSpace(settings.FancyZonesExcludedApps))
            {
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    string name = proc.ProcessName;
                    string[] excluded = settings.FancyZonesExcludedApps
                        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string ex in excluded)
                    {
                        if (name.Equals(ex.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            rejectReason = $"ExcludedApp({name})";
                            return false;
                        }
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
            // Prefer the physical button state so the overlay still responds if the hook
            // callback is delayed; the hook state covers the interval around swallowed events.
            bool rightDown = rightButtonDown || NativeMethods.IsKeyDown(NativeMethods.VK_RBUTTON);
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

        // ── DEBUG 日志 ──

        private void WriteDebugLog(string message)
        {
            if (!settings.FancyZonesDebugLog) return;
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(DebugLogPath, line);
            }
            catch
            {
                // Ignore logging failures.
            }
        }

        private static string DescribeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "hwnd=0x0";
            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            string cls = NativeMethods.GetWindowClass(hwnd);
            string title = NativeMethods.GetWindowTitle(hwnd);
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            string procName = "?";
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                procName = proc.ProcessName;
            }
            catch (Exception ex)
            {
                procName = $"<err:{ex.GetType().Name}>";
            }

            NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT r);
            return $"hwnd=0x{hwnd:X} root=0x{root:X} pid={pid} proc=\"{procName}\" class=\"{cls}\" title=\"{title}\" style=0x{style:X8} exStyle=0x{exStyle:X8} rect={r.ToRectangle()}";
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
