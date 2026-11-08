using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using ScreenCaptureTool.FancyZones;

namespace ScreenCaptureTool
{
    public partial class MainForm : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private Settings settings;
        private const int HOTKEY_ID = 1;
        private const int CANCEL_HOTKEY_ID = 2;
        private const int STAMP_HOTKEY_ID = 3;
        private const int ONE_KEY_ADJUST_HOTKEY_ID = 4;
        private const uint MOD_NOREPEAT = 0x4000;
        private bool cancelHotkeyRegistered;
        private bool captureInProgress;
        private bool isExiting;
        private bool resourcesCleaned;
        private bool oneKeyAdjustHotkeyRegistered;
        private ZoneStore zoneStore;
        private FancyZonesManager fancyZonesManager;
        private LayoutEditorForm? layoutEditorForm;
        private ToolStripMenuItem oneKeyAdjustMenuItem;

        private TextBox txtFancyZonesHotkey;
        private ComboBox cmbFancyZonesActivationKey;
        private TextBox txtFancyZonesSpacing;
        private CheckBox chkFancyZonesRestoreSize;
        private TextBox txtFancyZonesExcludedApps;
        private Button btnOpenLayoutEditor;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private Label lblHotkey;
        private TextBox txtHotkey;
        private Label lblCancelHotkey;
        private TextBox txtCancelHotkey;
        private Label lblStampHotkey;
        private TextBox txtStampHotkey;
        private Label lblOpacity;
        private TextBox txtOpacity;
        private Label lblBorder;
        private TextBox txtBorder;
        private Label lblSimilarity;
        private TextBox txtSimilarity;
        private Label lblColorSimilarity;
        private TextBox txtColorSimilarity;
        private Label lblMaxMatch;
        private TextBox txtMaxMatch;
        private Label lblMarkerBorder;
        private TextBox txtMarkerBorder;
        private Label lblMarkerAlpha;
        private TextBox txtMarkerAlpha;
        private Label lblMagnifierSize;
        private TextBox txtMagnifierSize;
        private Label lblMagnifierZoom;
        private TextBox txtMagnifierZoom;
        private Label lblMagnifierFont;
        private TextBox txtMagnifierFont;
        private Label lblStampWidth;
        private TextBox txtStampWidth;
        private Label lblStampHeight;
        private TextBox txtStampHeight;
        private Label lblStampStep;
        private TextBox txtStampStep;
        private Button btnApply;
        private Button btnCapture;
        private Panel settingsPanel;

        public MainForm()
        {
            InitializeComponent();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            settings = Settings.Load();
            zoneStore = ZoneStore.Load();
            fancyZonesManager = new FancyZonesManager(settings, zoneStore);
            OverlayForm.OpenOverlayStateChanged += OverlayForm_OpenOverlayStateChanged;

            this.Text = "抓屏软件设置";
            this.Size = new Size(460, 790);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            SetupTrayIcon();
            SetupUI();
        }

        private void SetupUI()
        {
            const int labelX = 20;
            const int inputX = 305;
            const int inputW = 100;
            const int labelW = 275;
            const int rowH = 28;

            settingsPanel = new Panel
            {
                AutoScroll = true,
                Location = new Point(0, 0),
                Size = this.ClientSize,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            int y = 8;

            // === 快捷键组 ===
            lblHotkey = new Label { Text = "启动快捷键:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtHotkey = new TextBox {
                Text = settings.Hotkey, Location = new Point(inputX, y), Size = new Size(inputW, 23),
                ReadOnly = true, BackColor = Color.White, TextAlign = HorizontalAlignment.Center
            };
            txtHotkey.KeyDown += TxtHotkey_KeyDown;
            y += rowH;

            lblCancelHotkey = new Label { Text = "取消快捷键:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtCancelHotkey = new TextBox {
                Text = settings.CancelHotkey, Location = new Point(inputX, y), Size = new Size(inputW, 23),
                ReadOnly = true, BackColor = Color.White, TextAlign = HorizontalAlignment.Center
            };
            txtCancelHotkey.KeyDown += TxtCancelHotkey_KeyDown;
            y += rowH;

            lblStampHotkey = new Label { Text = "印章模式快捷键:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtStampHotkey = new TextBox {
                Text = settings.StampHotkey, Location = new Point(inputX, y), Size = new Size(inputW, 23),
                ReadOnly = true, BackColor = Color.White, TextAlign = HorizontalAlignment.Center
            };
            txtStampHotkey.KeyDown += TxtStampHotkey_KeyDown;
            y += rowH + 6;

            // === 匹配参数组 ===
            lblSimilarity = new Label { Text = "相似度 (50-99)%:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtSimilarity = new TextBox { Text = settings.SimilarityThresholdPercent.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH;

            lblColorSimilarity = new Label { Text = "颜色相似度(HSV, 0.10-1.00):", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtColorSimilarity = new TextBox { Text = settings.ColorSimilarityThreshold.ToString("0.00", CultureInfo.InvariantCulture), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH;

            lblMaxMatch = new Label { Text = "最大匹配数 (1-50):", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtMaxMatch = new TextBox { Text = settings.MaxMatchResults.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH + 6;

            // === 显示参数组 ===
            lblOpacity = new Label { Text = "初始透明度 (10-100)%:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtOpacity = new TextBox { Text = settings.DefaultOpacity.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH;

            lblBorder = new Label { Text = "边框大小 (0-20)px:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtBorder = new TextBox { Text = settings.BorderSize.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH + 6;

            // === 标记参数组 ===
            lblMarkerBorder = new Label { Text = "标记框粗细 (1-10)px:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtMarkerBorder = new TextBox { Text = settings.MarkerBorderThickness.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH;

            lblMarkerAlpha = new Label { Text = "标记框透明度 (20-200):", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtMarkerAlpha = new TextBox { Text = settings.MarkerFillAlpha.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH + 6;

            // === 放大镜参数组 ===
            lblMagnifierSize = new Label { Text = "放大镜大小 (80-260)px:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtMagnifierSize = new TextBox { Text = settings.MagnifierSize.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH;

            lblMagnifierZoom = new Label { Text = "放大倍数 (2-15)x:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtMagnifierZoom = new TextBox { Text = settings.MagnifierZoom.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH;

            lblMagnifierFont = new Label { Text = "放大镜文字大小 (6-20):", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtMagnifierFont = new TextBox { Text = settings.MagnifierFontSize.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH + 6;

            // === 印章参数组 ===
            lblStampWidth = new Label { Text = "印章框宽度 (20-800)px:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtStampWidth = new TextBox { Text = settings.StampBoxWidth.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH;

            lblStampHeight = new Label { Text = "印章框高度 (20-800)px:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtStampHeight = new TextBox { Text = settings.StampBoxHeight.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH;

            lblStampStep = new Label { Text = "滚轮缩放步进 (5-30)%:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtStampStep = new TextBox { Text = settings.StampWheelScaleStepPercent.ToString(), Location = new Point(inputX, y), Size = new Size(inputW, 23) };
            y += rowH + 12;

            // === 窗口区域布局 (FancyZones) ===
            var lblZonesHotkey = new Label { Text = "一键调整窗口快捷键:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtFancyZonesHotkey = new TextBox
            {
                Text = settings.FancyZonesEditorHotkey,
                Location = new Point(inputX, y),
                Size = new Size(inputW, 23),
                ReadOnly = true,
                BackColor = Color.White,
                TextAlign = HorizontalAlignment.Center
            };
            txtFancyZonesHotkey.KeyDown += TxtFancyZonesHotkey_KeyDown;
            y += rowH;

            var lblActivationKey = new Label { Text = "按住激活键+右键显示区域:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            cmbFancyZonesActivationKey = new ComboBox
            {
                Location = new Point(inputX, y),
                Size = new Size(inputW, 23),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbFancyZonesActivationKey.Items.AddRange(new object[] { "Shift", "Ctrl", "Alt" });
            cmbFancyZonesActivationKey.SelectedIndex = Math.Clamp(settings.FancyZonesActivationKey, 0, 2);
            y += rowH;

            var lblSpacing = new Label { Text = "区域间距 (0-50)px:", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtFancyZonesSpacing = new TextBox
            {
                Text = settings.FancyZonesSpacing.ToString(),
                Location = new Point(inputX, y),
                Size = new Size(inputW, 23)
            };
            y += rowH;

            chkFancyZonesRestoreSize = new CheckBox
            {
                Text = "拖出区域时恢复窗口原尺寸",
                Checked = settings.FancyZonesRestoreSize,
                Location = new Point(labelX, y + 2),
                Size = new Size(labelW, 22)
            };
            y += rowH;

            var lblExcludedApps = new Label { Text = "排除程序 (逗号分隔):", Location = new Point(labelX, y + 2), Size = new Size(labelW, 20) };
            txtFancyZonesExcludedApps = new TextBox
            {
                Text = settings.FancyZonesExcludedApps,
                Location = new Point(inputX, y),
                Size = new Size(inputW, 23)
            };
            y += rowH + 6;

            btnOpenLayoutEditor = new Button
            {
                Text = "打开区域布局编辑器",
                Location = new Point(labelX, y),
                Size = new Size(180, 32),
                BackColor = Color.LightSteelBlue
            };
            btnOpenLayoutEditor.Click += (s, e) => OpenLayoutEditor();
            y += 40;

            // === 按钮 ===
            btnApply = new Button { Text = "保存并隐藏", Location = new Point(labelX, y), Size = new Size(130, 38), BackColor = Color.LightGray };
            btnApply.Click += (s, e) => {
                if (!TryReadInt("相似度", txtSimilarity, 50, 99, out int similarity)) return;
                if (!TryReadDouble("颜色相似度", txtColorSimilarity, 0.10, 1.00, out double colorSimilarity)) return;
                if (!TryReadInt("最大匹配数", txtMaxMatch, 1, 50, out int maxMatch)) return;
                if (!TryReadInt("初始透明度", txtOpacity, 10, 100, out int opacity)) return;
                if (!TryReadInt("边框大小", txtBorder, 0, 20, out int border)) return;
                if (!TryReadInt("标记框粗细", txtMarkerBorder, 1, 10, out int markerBorder)) return;
                if (!TryReadInt("标记框透明度", txtMarkerAlpha, 20, 200, out int markerAlpha)) return;
                if (!TryReadInt("放大镜大小", txtMagnifierSize, 80, 260, out int magSize)) return;
                if (!TryReadInt("放大倍数", txtMagnifierZoom, 2, 15, out int magZoom)) return;
                if (!TryReadInt("放大镜文字大小", txtMagnifierFont, 8, 20, out int magFont)) return;
                if (!TryReadInt("印章框宽度", txtStampWidth, 20, 800, out int stampWidth)) return;
                if (!TryReadInt("印章框高度", txtStampHeight, 20, 800, out int stampHeight)) return;
                if (!TryReadInt("滚轮缩放步进", txtStampStep, 5, 30, out int stampStep)) return;
                if (!TryReadInt("区域间距", txtFancyZonesSpacing, 0, 50, out int zoneSpacing)) return;

                settings.SimilarityThresholdPercent = similarity;
                settings.ColorSimilarityThreshold = colorSimilarity;
                settings.MaxMatchResults = maxMatch;
                settings.DefaultOpacity = opacity;
                settings.BorderSize = border;
                settings.MarkerBorderThickness = markerBorder;
                settings.MarkerFillAlpha = markerAlpha;
                settings.MagnifierSize = magSize;
                settings.MagnifierZoom = magZoom;
                settings.MagnifierFontSize = magFont;
                settings.StampBoxWidth = stampWidth;
                settings.StampBoxHeight = stampHeight;
                settings.StampWheelScaleStepPercent = stampStep;
                settings.FancyZonesActivationKey = Math.Clamp(cmbFancyZonesActivationKey.SelectedIndex, 0, 2);
                settings.FancyZonesSpacing = zoneSpacing;
                settings.FancyZonesRestoreSize = chkFancyZonesRestoreSize.Checked;
                settings.FancyZonesExcludedApps = txtFancyZonesExcludedApps.Text.Trim();
                settings.Save();
                UpdateHotKey();
                OverlayForm.ApplySettingsToOpenOverlays(settings);
                this.Hide();
                trayIcon.ShowBalloonTip(2000, "设置已保存", $"程序已隐藏，按 {settings.Hotkey} 开始截图", ToolTipIcon.Info);
            };

            btnCapture = new Button { Text = "立即截图", Location = new Point(inputX - 50, y), Size = new Size(130, 38), BackColor = Color.LightBlue };
            btnCapture.Click += (s, e) => StartCapture(false);

            settingsPanel.Controls.Add(lblHotkey);
            settingsPanel.Controls.Add(txtHotkey);
            settingsPanel.Controls.Add(lblCancelHotkey);
            settingsPanel.Controls.Add(txtCancelHotkey);
            settingsPanel.Controls.Add(lblStampHotkey);
            settingsPanel.Controls.Add(txtStampHotkey);
            settingsPanel.Controls.Add(lblSimilarity);
            settingsPanel.Controls.Add(txtSimilarity);
            settingsPanel.Controls.Add(lblColorSimilarity);
            settingsPanel.Controls.Add(txtColorSimilarity);
            settingsPanel.Controls.Add(lblMaxMatch);
            settingsPanel.Controls.Add(txtMaxMatch);
            settingsPanel.Controls.Add(lblOpacity);
            settingsPanel.Controls.Add(txtOpacity);
            settingsPanel.Controls.Add(lblBorder);
            settingsPanel.Controls.Add(txtBorder);
            settingsPanel.Controls.Add(lblMarkerBorder);
            settingsPanel.Controls.Add(txtMarkerBorder);
            settingsPanel.Controls.Add(lblMarkerAlpha);
            settingsPanel.Controls.Add(txtMarkerAlpha);
            settingsPanel.Controls.Add(lblMagnifierSize);
            settingsPanel.Controls.Add(txtMagnifierSize);
            settingsPanel.Controls.Add(lblMagnifierZoom);
            settingsPanel.Controls.Add(txtMagnifierZoom);
            settingsPanel.Controls.Add(lblMagnifierFont);
            settingsPanel.Controls.Add(txtMagnifierFont);
            settingsPanel.Controls.Add(lblStampWidth);
            settingsPanel.Controls.Add(txtStampWidth);
            settingsPanel.Controls.Add(lblStampHeight);
            settingsPanel.Controls.Add(txtStampHeight);
            settingsPanel.Controls.Add(lblStampStep);
            settingsPanel.Controls.Add(txtStampStep);
            settingsPanel.Controls.Add(lblZonesHotkey);
            settingsPanel.Controls.Add(txtFancyZonesHotkey);
            settingsPanel.Controls.Add(lblActivationKey);
            settingsPanel.Controls.Add(cmbFancyZonesActivationKey);
            settingsPanel.Controls.Add(lblSpacing);
            settingsPanel.Controls.Add(txtFancyZonesSpacing);
            settingsPanel.Controls.Add(chkFancyZonesRestoreSize);
            settingsPanel.Controls.Add(lblExcludedApps);
            settingsPanel.Controls.Add(txtFancyZonesExcludedApps);
            settingsPanel.Controls.Add(btnOpenLayoutEditor);
            settingsPanel.Controls.Add(btnApply);
            settingsPanel.Controls.Add(btnCapture);
            this.Controls.Add(settingsPanel);
        }

        private bool TryReadInt(string label, TextBox textBox, int min, int max, out int value)
        {
            if (!int.TryParse(textBox.Text.Trim(), out value))
            {
                MessageBox.Show($"{label} 请输入数字。");
                textBox.Focus();
                return false;
            }
            if (value < min || value > max)
            {
                MessageBox.Show($"{label} 取值范围 {min}-{max}。");
                textBox.Focus();
                return false;
            }
            return true;
        }

        private bool TryReadDouble(string label, TextBox textBox, double min, double max, out double value)
        {
            string text = textBox.Text.Trim();
            bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                          || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            if (!parsed)
            {
                MessageBox.Show($"{label} 请输入数字（例如 0.50）。");
                textBox.Focus();
                return false;
            }

            if (value < min || value > max)
            {
                MessageBox.Show($"{label} 取值范围 {min:0.00}-{max:0.00}。");
                textBox.Focus();
                return false;
            }

            return true;
        }

        private void TxtHotkey_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape || IsModifierKey(e.KeyCode)) return;

            string hotkeyText = "";
            uint modifiers = 0;
            if (e.Control) { hotkeyText += "Ctrl + "; modifiers |= 2; }
            if (e.Alt) { hotkeyText += "Alt + "; modifiers |= 1; }
            if (e.Shift) { hotkeyText += "Shift + "; modifiers |= 4; }
            
            hotkeyText += e.KeyCode.ToString();
            
            settings.Hotkey = hotkeyText;
            settings.HotkeyModifiers = modifiers;
            settings.HotkeyCode = (uint)e.KeyCode;
            txtHotkey.Text = hotkeyText;
        }

        private void TxtCancelHotkey_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.None || IsModifierKey(e.KeyCode)) return;

            string hotkeyText = "";
            uint modifiers = 0;
            if (e.Control) { hotkeyText += "Ctrl + "; modifiers |= 2; }
            if (e.Alt) { hotkeyText += "Alt + "; modifiers |= 1; }
            if (e.Shift) { hotkeyText += "Shift + "; modifiers |= 4; }

            hotkeyText += e.KeyCode.ToString();

            settings.CancelHotkey = hotkeyText;
            settings.CancelHotkeyModifiers = modifiers;
            settings.CancelHotkeyCode = (uint)e.KeyCode;
            txtCancelHotkey.Text = hotkeyText;
        }

        private void TxtStampHotkey_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.None || IsModifierKey(e.KeyCode)) return;

            string hotkeyText = "";
            uint modifiers = 0;
            if (e.Control) { hotkeyText += "Ctrl + "; modifiers |= 2; }
            if (e.Alt) { hotkeyText += "Alt + "; modifiers |= 1; }
            if (e.Shift) { hotkeyText += "Shift + "; modifiers |= 4; }

            hotkeyText += e.KeyCode.ToString();

            settings.StampHotkey = hotkeyText;
            settings.StampHotkeyModifiers = modifiers;
            settings.StampHotkeyCode = (uint)e.KeyCode;
            txtStampHotkey.Text = hotkeyText;
        }

        private void TxtFancyZonesHotkey_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape || IsModifierKey(e.KeyCode))
            {
                return;
            }

            string hotkeyText = "";
            uint modifiers = 0;
            if (e.Control) { hotkeyText += "Ctrl + "; modifiers |= 2; }
            if (e.Alt) { hotkeyText += "Alt + "; modifiers |= 1; }
            if (e.Shift) { hotkeyText += "Shift + "; modifiers |= 4; }
            hotkeyText += e.KeyCode.ToString();

            settings.FancyZonesEditorHotkey = hotkeyText;
            settings.FancyZonesEditorHotkeyModifiers = modifiers;
            settings.FancyZonesEditorHotkeyCode = (uint)e.KeyCode;
            txtFancyZonesHotkey.Text = hotkeyText;
        }

        private static bool IsModifierKey(Keys keyCode)
        {
            return keyCode == Keys.ControlKey || keyCode == Keys.ShiftKey || keyCode == Keys.Menu ||
                   keyCode == Keys.LWin || keyCode == Keys.RWin;
        }

        private void SetupTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示设置", null, (s, e) => this.ShowAndActivate());
            trayMenu.Items.Add("立即截图 (F1)", null, (s, e) => StartCapture(false));
            trayMenu.Items.Add("印章截图 (F2)", null, (s, e) => StartCapture(true));
            trayMenu.Items.Add("区域布局编辑器", null, (s, e) => OpenLayoutEditor());
            oneKeyAdjustMenuItem = new ToolStripMenuItem("一键调整窗口")
            {
                Enabled = true
            };
            oneKeyAdjustMenuItem.Click += (s, e) => ApplyCurrentLayoutWindows();
            trayMenu.Items.Add(oneKeyAdjustMenuItem);
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("退出", null, (s, e) => ExitApplication());

            trayIcon = new NotifyIcon();
            trayIcon.Text = "抓屏软件 - 按 F1 截图";
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => this.ShowAndActivate();
        }

        private void ShowAndActivate()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.Activate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateHotKey();
        }

        private void UpdateHotKey()
        {
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            bool success = RegisterHotKey(this.Handle, HOTKEY_ID, settings.HotkeyModifiers, settings.HotkeyCode);
            if (!success)
            {
                MessageBox.Show($"无法注册热键 {settings.Hotkey}，可能已被占用。");
            }
            UnregisterHotKey(this.Handle, CANCEL_HOTKEY_ID);
            cancelHotkeyRegistered = false;
            SyncCancelHotKeyRegistration(true);
            UnregisterHotKey(this.Handle, STAMP_HOTKEY_ID);
            bool stampSuccess = RegisterHotKey(this.Handle, STAMP_HOTKEY_ID, settings.StampHotkeyModifiers, settings.StampHotkeyCode);
            if (!stampSuccess)
            {
                MessageBox.Show($"无法注册印章热键 {settings.StampHotkey}，可能已被占用。");
            }

            UpdateOneKeyAdjustHotkey(true);
        }

        private bool IsLayoutEditorOpen => layoutEditorForm != null &&
            !layoutEditorForm.IsDisposed && layoutEditorForm.Visible;

        private void UpdateOneKeyAdjustHotkey(bool enabled)
        {
            if (!this.IsHandleCreated || this.IsDisposed) return;

            UnregisterHotKey(this.Handle, ONE_KEY_ADJUST_HOTKEY_ID);
            oneKeyAdjustHotkeyRegistered = false;
            if (oneKeyAdjustMenuItem != null)
            {
                oneKeyAdjustMenuItem.Enabled = enabled;
            }
            if (!enabled) return;

            oneKeyAdjustHotkeyRegistered = RegisterHotKey(this.Handle, ONE_KEY_ADJUST_HOTKEY_ID,
                settings.FancyZonesEditorHotkeyModifiers | MOD_NOREPEAT,
                settings.FancyZonesEditorHotkeyCode);
            if (!oneKeyAdjustHotkeyRegistered)
            {
                MessageBox.Show($"无法注册一键调整窗口快捷键 {settings.FancyZonesEditorHotkey}，可能已被占用。");
            }
        }

        private bool ShouldEnableCancelHotKey()
        {
            return captureInProgress || OverlayForm.HasOpenOverlays;
        }

        private void SyncCancelHotKeyRegistration(bool showError)
        {
            if (!this.IsHandleCreated || this.IsDisposed)
            {
                return;
            }

            bool shouldEnable = ShouldEnableCancelHotKey();
            if (!shouldEnable)
            {
                if (cancelHotkeyRegistered)
                {
                    UnregisterHotKey(this.Handle, CANCEL_HOTKEY_ID);
                    cancelHotkeyRegistered = false;
                }
                return;
            }

            if (cancelHotkeyRegistered)
            {
                return;
            }

            bool cancelSuccess = RegisterHotKey(this.Handle, CANCEL_HOTKEY_ID, settings.CancelHotkeyModifiers, settings.CancelHotkeyCode);
            if (cancelSuccess)
            {
                cancelHotkeyRegistered = true;
            }
            else if (showError)
            {
                MessageBox.Show($"无法注册取消热键 {settings.CancelHotkey}，可能已被占用。");
            }
        }

        private void OverlayForm_OpenOverlayStateChanged()
        {
            if (this.IsDisposed)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)(() => SyncCancelHotKeyRegistration(false)));
                return;
            }

            SyncCancelHotKeyRegistration(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HOTKEY_ID)
            {
                StartCapture(false);
            }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == CANCEL_HOTKEY_ID)
            {
                OverlayForm.CloseAllOpen();
                SyncCancelHotKeyRegistration(false);
            }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == STAMP_HOTKEY_ID)
            {
                StartCapture(true);
            }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == ONE_KEY_ADJUST_HOTKEY_ID)
            {
                ApplyCurrentLayoutWindows();
            }
            base.WndProc(ref m);
        }

        private void OpenLayoutEditor()
        {
            if (layoutEditorForm != null && !layoutEditorForm.IsDisposed)
            {
                layoutEditorForm.Show();
                if (layoutEditorForm.WindowState == FormWindowState.Minimized)
                {
                    layoutEditorForm.WindowState = FormWindowState.Normal;
                }
                layoutEditorForm.Activate();
                return;
            }

            layoutEditorForm = new LayoutEditorForm(zoneStore, settings, fancyZonesManager);
            LayoutEditorForm editor = layoutEditorForm;
            editor.FormClosed += (s, e) =>
            {
                if (ReferenceEquals(layoutEditorForm, editor))
                {
                    layoutEditorForm = null;
                }
            };
            layoutEditorForm.Show();
            layoutEditorForm.Activate();
        }

        private void ApplyCurrentLayoutWindows()
        {
            ZoneLayout? layout = IsLayoutEditorOpen
                ? layoutEditorForm!.CurrentLayout
                : zoneStore.ActiveLayout;
            Screen targetScreen = IsLayoutEditorOpen
                ? Screen.FromControl(layoutEditorForm!)
                : Screen.FromPoint(Cursor.Position);
            IWin32Window? owner = IsLayoutEditorOpen
                ? layoutEditorForm
                : null;
            fancyZonesManager.ApplyLayoutWindows(layout, targetScreen, owner);
        }

        private void StartCapture(bool stampMode)
        {
            if (captureInProgress) return;
            captureInProgress = true;
            SyncCancelHotKeyRegistration(true);
            this.Hide();
            try
            {
                using (var captureForm = new CaptureForm(settings, stampMode))
                {
                    if (captureForm.ShowDialog() == DialogResult.OK)
                    {
                        var overlay = new OverlayForm(captureForm.SelectedImage, captureForm.SelectedRegion, settings, stampMode);
                        overlay.Show();
                    }
                }
            }
            finally
            {
                captureInProgress = false;
                SyncCancelHotKeyRegistration(false);
            }
        }

        private void ExitApplication()
        {
            if (isExiting) return;
            isExiting = true;
            try
            {
                if (layoutEditorForm != null && !layoutEditorForm.IsDisposed)
                {
                    layoutEditorForm.Close();
                    layoutEditorForm.Dispose();
                    layoutEditorForm = null;
                }
            }
            catch
            {
            }
            try
            {
                OverlayForm.CloseAllOpen();
            }
            catch
            {
            }
            CleanupResources();
            Application.Exit();
        }

        private void CleanupResources()
        {
            if (resourcesCleaned) return;
            resourcesCleaned = true;

            OverlayForm.OpenOverlayStateChanged -= OverlayForm_OpenOverlayStateChanged;
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                try
                {
                    UnregisterHotKey(this.Handle, HOTKEY_ID);
                    UnregisterHotKey(this.Handle, CANCEL_HOTKEY_ID);
                    cancelHotkeyRegistered = false;
                    UnregisterHotKey(this.Handle, STAMP_HOTKEY_ID);
                    UnregisterHotKey(this.Handle, ONE_KEY_ADJUST_HOTKEY_ID);
                }
                catch
                {
                }
            }

            try
            {
                fancyZonesManager?.Dispose();
            }
            catch
            {
            }

            try
            {
                if (trayIcon != null)
                {
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                }
            }
            catch
            {
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!isExiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                CleanupResources();
            }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ClientSize = new System.Drawing.Size(428, 690);
            this.Name = "MainForm";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ResumeLayout(false);
        }
    }
}
