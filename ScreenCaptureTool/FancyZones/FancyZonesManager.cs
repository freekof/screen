using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenCaptureTool.FancyZones
{
    /// <summary>当前打开的顶层应用窗口信息。</summary>
    public sealed class AppWindowInfo
    {
        public IntPtr Hwnd { get; init; }
        public uint ProcessId { get; init; }
        public string ProcessName { get; init; } = "";
        public string Title { get; init; } = "";
        public Rectangle Bounds { get; init; }

        public string DisplayText =>
            string.IsNullOrWhiteSpace(Title)
                ? $"{ProcessName} [0x{Hwnd:X}]"
                : $"{Title} ({Bounds.Width}×{Bounds.Height})";

        public override string ToString() => DisplayText;
    }

    /// <summary>区域程序下拉菜单项。</summary>
    public sealed class AppProgramItem
    {
        public string ProcessName { get; }
        public string DisplayText { get; }

        public AppProgramItem(string processName, string displayText)
        {
            ProcessName = processName;
            DisplayText = displayText;
        }

        public override string ToString() => DisplayText;
    }

    /// <summary>
    /// FancyZones 主管理器：拖动检测 → 区域高亮 → 窗口吸附，以及一键按布局调整指定程序窗口。
    /// 仅在区域布局编辑器打开时启动钩子监测，关闭编辑器后立即释放钩子，避免后台占用系统资源。
    /// </summary>
    public sealed class FancyZonesManager : IDisposable
    {
        private readonly Settings settings;
        private readonly ZoneStore store;
        private ZoneOverlayForm? overlay;

        // WinEvent 钩子与鼠标钩子（必须保持委托引用以防 GC）
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
        private long lastChordActiveTick;
        private readonly HashSet<int> lastActiveHighlight = new HashSet<int>();
        private int anchorZone = -1; // Ctrl 多选锚点
        private Screen? overlayScreen;
        private System.Windows.Forms.Timer? pollTimer;

        private const int ChordReleaseGraceMs = 300;

        // 吸附记忆
        private readonly Dictionary<IntPtr, Rectangle> originalSizes = new Dictionary<IntPtr, Rectangle>();
        private readonly uint currentPid;
        private bool isDisposed;
        private bool isSuspended;

        public uint CurrentProcessId => currentPid;

        public FancyZonesManager(Settings settings, ZoneStore store)
        {
            this.settings = settings;
            this.store = store;
            using (var proc = Process.GetCurrentProcess())
            {
                currentPid = (uint)proc.Id;
            }
        }

        // ── 启动 / 停止（仅在区域布局编辑器打开期间启用） ──

        public void Start()
        {
            if (isDisposed || hookStart != IntPtr.Zero) return;
            isSuspended = false;

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
        }

        public void Stop()
        {
            StopPolling();
            dragActive = false;
            isCustomDrag = false;
            lastActiveHighlight.Clear();
            HideOverlay();
            if (hookStart != IntPtr.Zero) { NativeMethods.UnhookWinEvent(hookStart); hookStart = IntPtr.Zero; }
            if (hookEnd != IntPtr.Zero) { NativeMethods.UnhookWinEvent(hookEnd); hookEnd = IntPtr.Zero; }
            if (mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
            mouseHookProc = null;
            winEventProc = null;
            rightButtonDown = false;
            suppressRightButtonRelease = false;
        }

        public void SetSuspended(bool suspended)
        {
            isSuspended = suspended;
            if (suspended)
            {
                StopPolling();
                dragActive = false;
                isCustomDrag = false;
                lastActiveHighlight.Clear();
                HideOverlay();
            }
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
                }
            }
        }

        // ── WinEvent 回调 ──

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (isSuspended || idObject != 0 || hwnd == IntPtr.Zero) return;

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
            if (!isSuspended && nCode >= NativeMethods.HC_ACTION)
            {
                int message = wParam.ToInt32();
                if (message == NativeMethods.WM_RBUTTONDOWN || message == NativeMethods.WM_RBUTTONDBLCLK)
                {
                    rightButtonDown = true;

                    // 支持雷电模拟器等不触发 EVENT_SYSTEM_MOVESIZESTART 的自绘窗口：
                    // 当左键按住拖动窗口期间按下右键，立即接管为自定义拖动并吞掉右键消息。
                    if (!dragActive && NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON))
                    {
                        TryStartCustomDragFromForeground();
                    }

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
                else if (message == NativeMethods.WM_LBUTTONDOWN)
                {
                    // 若存在曾被吸附过的自绘窗口，按下左键时检查是否为该窗口以便拖出时恢复原尺寸
                    if (!dragActive && originalSizes.Count > 0 && settings.FancyZonesRestoreSize)
                    {
                        IntPtr fg = GetForegroundRootWindow();
                        if (fg != IntPtr.Zero && originalSizes.ContainsKey(fg))
                        {
                            TryStartCustomDrag(fg);
                        }
                    }
                }
            }

            return NativeMethods.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        private static IntPtr GetForegroundRootWindow()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = NativeMethods.GetAncestor(fg, NativeMethods.GA_ROOT);
            return root != IntPtr.Zero ? root : fg;
        }

        private void TryStartCustomDragFromForeground()
        {
            IntPtr hwnd = GetForegroundRootWindow();
            if (hwnd == IntPtr.Zero) return;
            TryStartCustomDrag(hwnd);
        }

        private void TryStartCustomDrag(IntPtr hwnd)
        {
            if (!IsEligibleWindow(hwnd)) return;
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT wr)) return;

            Rectangle currentRect = wr.ToRectangle();
            if (settings.FancyZonesRestoreSize && originalSizes.TryGetValue(hwnd, out Rectangle originalSize))
            {
                currentRect = RestoreWindowUnderCursor(hwnd, currentRect, originalSize);
            }

            BeginDragTracking(hwnd, currentRect, customDrag: true);
        }

        private void OnMoveSizeStart(IntPtr hwnd)
        {
            IntPtr rootHwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (rootHwnd != IntPtr.Zero)
            {
                hwnd = rootHwnd;
            }

            if (!IsEligibleWindow(hwnd)) return;

            // 判断是 Move 还是 Resize（使用 WM_NCHITTEST）
            NativeMethods.GetCursorPos(out NativeMethods.POINT pt);
            IntPtr lParam = (IntPtr)((pt.Y << 16) | (pt.X & 0xFFFF));
            NativeMethods.SendMessageTimeout(hwnd, NativeMethods.WM_NCHITTEST,
                IntPtr.Zero, lParam, NativeMethods.SMTO_ABORTIFHUNG, 100, out IntPtr result);
            int ht = result.ToInt32();
            if (NativeMethods.IsSizeHitTest(ht)) return;

            rightButtonDown = NativeMethods.IsKeyDown(NativeMethods.VK_RBUTTON);

            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT wr)) return;
            Rectangle currentRect = wr.ToRectangle();
            if (settings.FancyZonesRestoreSize && originalSizes.TryGetValue(hwnd, out Rectangle originalSize))
            {
                currentRect = RestoreWindowUnderCursor(hwnd, currentRect, originalSize);
            }

            BeginDragTracking(hwnd, currentRect, customDrag: false);
        }

        private static Rectangle RestoreWindowUnderCursor(IntPtr hwnd, Rectangle currentRect, Rectangle originalSize)
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
            if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT wr))
            {
                return wr.ToRectangle();
            }
            return new Rectangle(restoredX, restoredY, originalSize.Width, originalSize.Height);
        }

        private void BeginDragTracking(IntPtr hwnd, Rectangle startRect, bool customDrag)
        {
            dragHwnd = hwnd;
            dragStartRect = startRect;
            dragActive = true;
            isCustomDrag = customDrag;
            startWasMaximized = NativeMethods.IsZoomed(hwnd);
            resizeDetected = false;
            anchorZone = -1;
            lastChordActiveTick = 0;
            lastActiveHighlight.Clear();
            overlayShowing = false;
            overlayScreen = null;

            StartPolling();
        }

        private void OnMoveSizeEnd(IntPtr hwnd)
        {
            if (!dragActive || hwnd != dragHwnd) return;
            bool wasOverlayShowing = overlayShowing;
            bool wasResizeDetected = resizeDetected;
            long elapsedSinceChord = Environment.TickCount64 - lastChordActiveTick;
            bool chordRecentlyActive = IsActivationChordDown() ||
                                       (lastChordActiveTick > 0 && elapsedSinceChord <= ChordReleaseGraceMs);

            dragActive = false;
            isCustomDrag = false;
            StopPolling();

            try
            {
                if (wasResizeDetected)
                {
                    HideOverlay();
                    return;
                }

                if (!wasOverlayShowing || !chordRecentlyActive)
                {
                    HideOverlay();
                    RestoreOriginalSize(hwnd);
                    return;
                }

                var selected = GetCurrentHighlight();
                if (selected.Count == 0 && lastActiveHighlight.Count > 0 && elapsedSinceChord <= ChordReleaseGraceMs)
                {
                    selected = new HashSet<int>(lastActiveHighlight);
                }

                if (selected.Count == 0)
                {
                    HideOverlay();
                    RestoreOriginalSize(hwnd);
                    return;
                }

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
                lastActiveHighlight.Clear();
                HideOverlay();
            }
        }

        // ── 轮询（仅在活动拖动期间运行，闲置时停止） ──

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
            if (!dragActive || dragHwnd == IntPtr.Zero || isSuspended)
            {
                StopPolling();
                HideOverlay();
                return;
            }

            if (!NativeMethods.IsWindow(dragHwnd))
            {
                dragActive = false;
                isCustomDrag = false;
                StopPolling();
                HideOverlay();
                return;
            }

            // 左键松开时结束拖动并执行吸附
            if (!NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON))
            {
                OnMoveSizeEnd(dragHwnd);
                return;
            }

            NativeMethods.GetWindowRect(dragHwnd, out NativeMethods.RECT nowRect);
            Rectangle now = nowRect.ToRectangle();
            bool sizeChanged = Math.Abs(now.Width - dragStartRect.Width) > 12 ||
                               Math.Abs(now.Height - dragStartRect.Height) > 12;
            if (sizeChanged && startWasMaximized && !NativeMethods.IsZoomed(dragHwnd))
            {
                dragStartRect = now;
                startWasMaximized = false;
            }
            else if (sizeChanged && !overlayShowing)
            {
                resizeDetected = true;
                HideOverlay();
                return;
            }

            // 检查激活键（Shift + 右键）
            bool activatorDown = IsActivationChordDown();
            if (!activatorDown)
            {
                // 给同时松开左右键留出 300ms 宽限期，防止右键比左键早松开几十毫秒导致取消
                if (overlayShowing && lastChordActiveTick > 0 &&
                    Environment.TickCount64 - lastChordActiveTick <= ChordReleaseGraceMs)
                {
                    return;
                }

                if (overlayShowing) HideOverlay();
                overlayShowing = false;
                anchorZone = -1;
                lastActiveHighlight.Clear();
                return;
            }

            lastChordActiveTick = Environment.TickCount64;

            // ESC 取消
            if (NativeMethods.IsKeyDown(NativeMethods.VK_ESCAPE))
            {
                dragActive = false;
                isCustomDrag = false;
                lastActiveHighlight.Clear();
                StopPolling();
                HideOverlay();
                return;
            }

            NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
            Point cursorPt = new Point(cursor.X, cursor.Y);

            Screen curScreen = Screen.FromPoint(cursorPt);
            ZoneLayout? layout = store.ActiveLayout;
            if (layout == null)
            {
                if (overlayShowing) HideOverlay();
                return;
            }

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

            lastActiveHighlight.Clear();
            foreach (int z in hlSet) lastActiveHighlight.Add(z);

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

        // ── 吸附与一键调整窗口 ──

        public void SnapWindow(IntPtr hwnd, Rectangle target, bool bringToFront = false)
        {
            if (!NativeMethods.IsWindow(hwnd)) return;

            if (!originalSizes.ContainsKey(hwnd) && NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT originalRect))
            {
                originalSizes[hwnd] = originalRect.ToRectangle();
            }

            if (NativeMethods.IsZoomed(hwnd) || NativeMethods.IsIconic(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            }

            NativeMethods.GetInvisibleBorders(hwnd, out int bLeft, out int bTop, out int bRight, out int bBottom);
            Rectangle adjusted = new Rectangle(
                target.X - bLeft,
                target.Y - bTop,
                target.Width + bLeft + bRight,
                target.Height + bTop + bBottom);

            uint flags = NativeMethods.SWP_NOACTIVATE;
            if (!bringToFront)
            {
                flags |= NativeMethods.SWP_NOZORDER;
            }

            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP,
                adjusted.X, adjusted.Y, adjusted.Width, adjusted.Height, flags);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP,
                adjusted.X, adjusted.Y, adjusted.Width, adjusted.Height, flags);
        }

        /// <summary>
        /// 按给定布局（或当前活动布局）将各区域指定的程序窗口一键移动到对应区域位置和大小。
        /// 如果某个指定程序存在多个窗口，则弹出下拉菜单供用户选择。
        /// </summary>
        public void ApplyLayoutWindows(ZoneLayout? layout = null, Screen? targetScreen = null, IWin32Window? owner = null)
        {
            layout ??= store.ActiveLayout;
            if (layout == null) return;

            Screen screen = targetScreen ?? Screen.FromPoint(Cursor.Position);
            List<Rectangle> zoneRects = layout.GetZoneRects(screen.WorkingArea, settings.FancyZonesSpacing);
            if (zoneRects.Count == 0) return;

            var zoneBindings = new List<(int ZoneIndex, Rectangle ZoneRect, string AppName)>();
            for (int i = 0; i < zoneRects.Count; i++)
            {
                string app = layout.GetZoneApp(i);
                if (!string.IsNullOrWhiteSpace(app))
                {
                    zoneBindings.Add((i, zoneRects[i], app));
                }
            }

            if (zoneBindings.Count == 0)
            {
                MessageBox.Show(owner,
                    "当前布局尚未为任何区域指定程序。\n请先在区域布局编辑器的区域尺寸后下拉菜单中选择程序。",
                    "一键调整窗口", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<AppWindowInfo> openWindows = GetOpenAppWindows(currentPid);
            var candidatesPerZone = new List<(int ZoneIndex, Rectangle ZoneRect, string AppName, List<AppWindowInfo> Windows)>();
            bool needSelectionDialog = false;

            // 统计同一个程序被多少个区域绑定
            var appZoneCounts = zoneBindings
                .GroupBy(b => b.AppName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var binding in zoneBindings)
            {
                var matches = openWindows
                    .Where(w => w.ProcessName.Equals(binding.AppName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count > 1)
                {
                    needSelectionDialog = true;
                }
                candidatesPerZone.Add((binding.ZoneIndex, binding.ZoneRect, binding.AppName, matches));
            }

            if (candidatesPerZone.All(c => c.Windows.Count == 0))
            {
                MessageBox.Show(owner,
                    "未找到已指定程序的运行窗口，请确认目标程序已打开。",
                    "一键调整窗口", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!needSelectionDialog)
            {
                foreach (var item in candidatesPerZone.OrderBy(c => c.ZoneIndex))
                {
                    if (item.Windows.Count == 1)
                    {
                        SnapWindow(item.Windows[0].Hwnd, item.ZoneRect, bringToFront: true);
                    }
                }
                return;
            }

            // 存在一个程序有多个窗口：弹出下拉菜单选择窗口
            using (var dlg = new Form())
            {
                dlg.Text = $"选择要调整的窗口 - {layout.Name}";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.TopMost = true;
                dlg.AutoScaleDimensions = new SizeF(96F, 96F);
                dlg.AutoScaleMode = AutoScaleMode.Dpi;

                var panel = new Panel
                {
                    Location = new Point(12, 12),
                    Size = new Size(496, 260),
                    AutoScroll = true
                };

                var combos = new List<(int ZoneIndex, Rectangle ZoneRect, ComboBox Combo)>();
                var usedCountByApp = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int y = 6;

                foreach (var item in candidatesPerZone)
                {
                    var lbl = new Label
                    {
                        Text = $"区域 {item.ZoneIndex + 1} ({item.ZoneRect.Width} × {item.ZoneRect.Height}) - {item.AppName}:",
                        Location = new Point(4, y + 4),
                        Size = new Size(220, 22)
                    };

                    var cmb = new ComboBox
                    {
                        Location = new Point(230, y),
                        Size = new Size(245, 24),
                        DropDownStyle = ComboBoxStyle.DropDownList
                    };

                    if (item.Windows.Count == 0)
                    {
                        cmb.Items.Add(new AppWindowInfo { Hwnd = IntPtr.Zero, ProcessName = item.AppName, Title = "(程序未运行)" });
                        cmb.SelectedIndex = 0;
                        cmb.Enabled = false;
                    }
                    else
                    {
                        foreach (var w in item.Windows)
                        {
                            cmb.Items.Add(w);
                        }
                        cmb.Items.Add(new AppWindowInfo { Hwnd = IntPtr.Zero, ProcessName = item.AppName, Title = "(不调整此区域)" });

                        usedCountByApp.TryGetValue(item.AppName, out int usedIdx);
                        cmb.SelectedIndex = Math.Min(usedIdx, item.Windows.Count - 1);
                        usedCountByApp[item.AppName] = usedIdx + 1;
                    }

                    panel.Controls.Add(lbl);
                    panel.Controls.Add(cmb);
                    combos.Add((item.ZoneIndex, item.ZoneRect, cmb));
                    y += 34;
                }

                int panelH = Math.Clamp(y + 8, 80, 300);
                panel.Size = new Size(496, panelH);

                var btnOk = new Button
                {
                    Text = "确定调整",
                    DialogResult = DialogResult.OK,
                    Location = new Point(310, panelH + 22),
                    Size = new Size(92, 32),
                    BackColor = Color.LightSkyBlue
                };
                var btnCancel = new Button
                {
                    Text = "取消",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(412, panelH + 22),
                    Size = new Size(88, 32)
                };

                dlg.ClientSize = new Size(520, panelH + 66);
                dlg.Controls.AddRange(new Control[] { panel, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(owner) == DialogResult.OK)
                {
                    foreach (var (zoneIndex, zoneRect, combo) in combos.OrderBy(c => c.ZoneIndex))
                    {
                        if (combo.SelectedItem is AppWindowInfo win && win.Hwnd != IntPtr.Zero)
                        {
                            SnapWindow(win.Hwnd, zoneRect, bringToFront: true);
                        }
                    }
                }
            }
        }

        // ── 枚举运行中的程序与窗口 ──

        public static List<AppWindowInfo> GetOpenAppWindows(uint excludePid = 0)
        {
            if (excludePid == 0)
            {
                using var self = Process.GetCurrentProcess();
                excludePid = (uint)self.Id;
            }

            var list = new List<AppWindowInfo>();
            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                if (!NativeMethods.IsWindow(hWnd) || !NativeMethods.IsWindowVisible(hWnd))
                    return true;

                IntPtr root = NativeMethods.GetAncestor(hWnd, NativeMethods.GA_ROOT);
                if (root != IntPtr.Zero && root != hWnd)
                    return true;

                int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_STYLE);
                int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
                if ((style & NativeMethods.WS_CHILD) != 0 || (style & NativeMethods.WS_DISABLED) != 0)
                    return true;
                if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 || (exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
                    return true;

                string title = NativeMethods.GetWindowTitle(hWnd).Trim();
                if (string.IsNullOrEmpty(title))
                    return true;

                string cls = NativeMethods.GetWindowClass(hWnd);
                if (cls.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0 || pid == excludePid)
                    return true;

                string procName = "";
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    procName = proc.ProcessName;
                }
                catch
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(procName) ||
                    procName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                NativeMethods.GetWindowRect(hWnd, out NativeMethods.RECT wr);
                list.Add(new AppWindowInfo
                {
                    Hwnd = hWnd,
                    ProcessId = pid,
                    ProcessName = procName,
                    Title = title,
                    Bounds = wr.ToRectangle()
                });
                return true;
            }, IntPtr.Zero);

            return list;
        }

        public static List<AppProgramItem> GetRunningProgramOptions(string? currentSelectedApp = null)
        {
            string selected = (currentSelectedApp ?? "").Trim();
            var options = new List<AppProgramItem>
            {
                new AppProgramItem("", "(未指定)")
            };

            var groups = GetOpenAppWindows()
                .GroupBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool foundSelected = string.IsNullOrEmpty(selected);
            foreach (var g in groups)
            {
                int count = g.Count();
                string firstTitle = g.First().Title;
                if (firstTitle.Length > 14) firstTitle = firstTitle.Substring(0, 14) + "…";
                string label = count == 1
                    ? $"{g.Key} ({firstTitle})"
                    : $"{g.Key} ({count}个窗口)";
                options.Add(new AppProgramItem(g.Key, label));
                if (!foundSelected && g.Key.Equals(selected, StringComparison.OrdinalIgnoreCase))
                {
                    foundSelected = true;
                }
            }

            if (!foundSelected && !string.IsNullOrEmpty(selected))
            {
                options.Add(new AppProgramItem(selected, $"{selected} (未运行)"));
            }

            return options;
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
            if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
                return false;

            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            if (root != IntPtr.Zero && root != hwnd)
                return false;

            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

            if ((style & NativeMethods.WS_CHILD) != 0 || (style & NativeMethods.WS_DISABLED) != 0)
                return false;
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 || (exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
                return false;

            string cls = NativeMethods.GetWindowClass(hwnd);
            if (cls.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                cls.Equals("SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
            {
                if (rect.Width < 80 || rect.Height < 80)
                    return false;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == currentPid) return false;

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
                            return false;
                    }
                }
                catch
                {
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
            bool rightDown = rightButtonDown || NativeMethods.IsKeyDown(NativeMethods.VK_RBUTTON);
            return IsActivatorDown() && rightDown;
        }

        private bool IsMultiSelectDown()
        {
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
