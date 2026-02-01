using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;

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

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private Label lblOpacity;
        private TextBox txtOpacity;
        private Label lblBorder;
        private TextBox txtBorder;
        private Label lblSimilarity;
        private TextBox txtSimilarity;
        private Label lblMarkerBorder;
        private TextBox txtMarkerBorder;
        private Label lblMarkerFont;
        private TextBox txtMarkerFont;
        private Label lblMarkerAlpha;
        private TextBox txtMarkerAlpha;
        private Label lblMagnifierSize;
        private TextBox txtMagnifierSize;
        private Label lblMagnifierZoom;
        private TextBox txtMagnifierZoom;
        private Label lblMagnifierFont;
        private TextBox txtMagnifierFont;
        private Label lblStampHotkey;
        private TextBox txtStampHotkey;
        private Label lblStampWidth;
        private TextBox txtStampWidth;
        private Label lblStampHeight;
        private TextBox txtStampHeight;
        private Label lblStampStep;
        private TextBox txtStampStep;
        private Label lblHotkey;
        private TextBox txtHotkey;
        private Label lblCancelHotkey;
        private TextBox txtCancelHotkey;
        private Button btnApply;
        private Button btnCapture;
        private Panel settingsPanel;

        public MainForm()
        {
            InitializeComponent();
            settings = Settings.Load();

            this.Text = "抓屏软件设置";
            this.Size = new Size(420, 640);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            SetupTrayIcon();
            SetupUI();
        }

        private void SetupUI()
        {
            int y = 20;
            settingsPanel = new Panel
            {
                AutoScroll = true,
                Location = new Point(0, 0),
                Size = this.ClientSize,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            
            lblHotkey = new Label { Text = "启动快捷键 (点击下方框后按键):", Location = new Point(20, y), Size = new Size(340, 20) };
            y += 25;
            txtHotkey = new TextBox {
                Text = settings.Hotkey,
                Location = new Point(20, y),
                Size = new Size(320, 25),
                ReadOnly = true,
                BackColor = Color.White,
                TextAlign = HorizontalAlignment.Center
            };
            txtHotkey.KeyDown += TxtHotkey_KeyDown;
            y += 40;

            lblCancelHotkey = new Label { Text = "取消快捷键 (点击下方框后按键):", Location = new Point(20, y), Size = new Size(340, 20) };
            y += 25;
            txtCancelHotkey = new TextBox {
                Text = settings.CancelHotkey,
                Location = new Point(20, y),
                Size = new Size(320, 25),
                ReadOnly = true,
                BackColor = Color.White,
                TextAlign = HorizontalAlignment.Center
            };
            txtCancelHotkey.KeyDown += TxtCancelHotkey_KeyDown;
            y += 35;

            lblStampHotkey = new Label { Text = "印章模式快捷键 (点击下方框后按键):", Location = new Point(20, y), Size = new Size(340, 20) };
            y += 25;
            txtStampHotkey = new TextBox {
                Text = settings.StampHotkey,
                Location = new Point(20, y),
                Size = new Size(320, 25),
                ReadOnly = true,
                BackColor = Color.White,
                TextAlign = HorizontalAlignment.Center
            };
            txtStampHotkey.KeyDown += TxtStampHotkey_KeyDown;
            y += 35;

            lblOpacity = new Label { Text = "初始透明度 (10-100)%:", Location = new Point(20, y), Size = new Size(220, 20) };
            txtOpacity = new TextBox { Text = settings.DefaultOpacity.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 30;

            lblBorder = new Label { Text = "边框大小 (0-20)px:", Location = new Point(20, y), Size = new Size(220, 20) };
            txtBorder = new TextBox { Text = settings.BorderSize.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 30;

            lblSimilarity = new Label { Text = "相似度 (50-99)%:", Location = new Point(20, y), Size = new Size(220, 20) };
            txtSimilarity = new TextBox { Text = settings.SimilarityThresholdPercent.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 30;

            lblMarkerBorder = new Label { Text = "标记框粗细 (1-10)px:", Location = new Point(20, y), Size = new Size(220, 20) };
            txtMarkerBorder = new TextBox { Text = settings.MarkerBorderThickness.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 30;

            lblMarkerFont = new Label { Text = "标记文字大小 (10-48):", Location = new Point(20, y), Size = new Size(220, 20) };
            txtMarkerFont = new TextBox { Text = settings.MarkerFontSize.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 30;

            lblMarkerAlpha = new Label { Text = "标记框透明度 (20-200):", Location = new Point(20, y), Size = new Size(220, 20) };
            txtMarkerAlpha = new TextBox { Text = settings.MarkerFillAlpha.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 30;

            lblMagnifierSize = new Label { Text = "放大镜大小 (80-260)px:", Location = new Point(20, y), Size = new Size(220, 20) };
            txtMagnifierSize = new TextBox { Text = settings.MagnifierSize.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 30;

            lblMagnifierZoom = new Label { Text = "放大倍数 (2-15)x:", Location = new Point(20, y), Size = new Size(220, 20) };
            txtMagnifierZoom = new TextBox { Text = settings.MagnifierZoom.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 30;

            lblMagnifierFont = new Label { Text = "放大镜文字大小 (8-20):", Location = new Point(20, y), Size = new Size(220, 20) };
            txtMagnifierFont = new TextBox { Text = settings.MagnifierFontSize.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 35;

            lblStampWidth = new Label { Text = "印章框宽度 (20-400)px:", Location = new Point(20, y), Size = new Size(220, 20) };
            txtStampWidth = new TextBox { Text = settings.StampBoxWidth.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 30;

            lblStampHeight = new Label { Text = "印章框高度 (20-400)px:", Location = new Point(20, y), Size = new Size(220, 20) };
            txtStampHeight = new TextBox { Text = settings.StampBoxHeight.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 30;

            lblStampStep = new Label { Text = "滚轮缩放步进 (5-30)%:", Location = new Point(20, y), Size = new Size(220, 20) };
            txtStampStep = new TextBox { Text = settings.StampWheelScaleStepPercent.ToString(), Location = new Point(250, y - 2), Size = new Size(90, 25) };
            y += 40;

            btnApply = new Button { Text = "保存并隐藏", Location = new Point(20, y), Size = new Size(120, 40), BackColor = Color.LightGray };
            btnApply.Click += (s, e) => {
                if (!TryReadInt("初始透明度", txtOpacity, 10, 100, out int opacity)) return;
                if (!TryReadInt("边框大小", txtBorder, 0, 20, out int border)) return;
                if (!TryReadInt("相似度", txtSimilarity, 50, 99, out int similarity)) return;
                if (!TryReadInt("标记框粗细", txtMarkerBorder, 1, 10, out int markerBorder)) return;
                if (!TryReadInt("标记文字大小", txtMarkerFont, 10, 48, out int markerFont)) return;
                if (!TryReadInt("标记框透明度", txtMarkerAlpha, 20, 200, out int markerAlpha)) return;
                if (!TryReadInt("放大镜大小", txtMagnifierSize, 80, 260, out int magSize)) return;
                if (!TryReadInt("放大倍数", txtMagnifierZoom, 2, 15, out int magZoom)) return;
                if (!TryReadInt("放大镜文字大小", txtMagnifierFont, 8, 20, out int magFont)) return;
                if (!TryReadInt("印章框宽度", txtStampWidth, 20, 400, out int stampWidth)) return;
                if (!TryReadInt("印章框高度", txtStampHeight, 20, 400, out int stampHeight)) return;
                if (!TryReadInt("滚轮缩放步进", txtStampStep, 5, 30, out int stampStep)) return;

                settings.DefaultOpacity = opacity;
                settings.BorderSize = border;
                settings.SimilarityThresholdPercent = similarity;
                settings.MarkerBorderThickness = markerBorder;
                settings.MarkerFontSize = markerFont;
                settings.MarkerFillAlpha = markerAlpha;
                settings.MagnifierSize = magSize;
                settings.MagnifierZoom = magZoom;
                settings.MagnifierFontSize = magFont;
                settings.StampBoxWidth = stampWidth;
                settings.StampBoxHeight = stampHeight;
                settings.StampWheelScaleStepPercent = stampStep;
                settings.Save();
                UpdateHotKey();
                this.Hide();
                trayIcon.ShowBalloonTip(2000, "设置已保存", $"程序已隐藏，按 {settings.Hotkey} 开始截图", ToolTipIcon.Info);
            };

            btnCapture = new Button { Text = "立即截图", Location = new Point(180, y), Size = new Size(120, 40), BackColor = Color.LightBlue };
            btnCapture.Click += (s, e) => StartCapture(false);

            settingsPanel.Controls.Add(lblHotkey);
            settingsPanel.Controls.Add(txtHotkey);
            settingsPanel.Controls.Add(lblCancelHotkey);
            settingsPanel.Controls.Add(txtCancelHotkey);
            settingsPanel.Controls.Add(lblStampHotkey);
            settingsPanel.Controls.Add(txtStampHotkey);
            settingsPanel.Controls.Add(lblOpacity);
            settingsPanel.Controls.Add(txtOpacity);
            settingsPanel.Controls.Add(lblBorder);
            settingsPanel.Controls.Add(txtBorder);
            settingsPanel.Controls.Add(lblSimilarity);
            settingsPanel.Controls.Add(txtSimilarity);
            settingsPanel.Controls.Add(lblMarkerBorder);
            settingsPanel.Controls.Add(txtMarkerBorder);
            settingsPanel.Controls.Add(lblMarkerFont);
            settingsPanel.Controls.Add(txtMarkerFont);
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

        private void TxtHotkey_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape) return;

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
            if (e.KeyCode == Keys.None) return;

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
            if (e.KeyCode == Keys.None) return;

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

        private void SetupTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示设置", null, (s, e) => this.ShowAndActivate());
            trayMenu.Items.Add("立即截图 (F1)", null, (s, e) => StartCapture(false));
            trayMenu.Items.Add("印章截图 (F2)", null, (s, e) => StartCapture(true));
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("退出", null, (s, e) => Application.Exit());

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
            bool cancelSuccess = RegisterHotKey(this.Handle, CANCEL_HOTKEY_ID, settings.CancelHotkeyModifiers, settings.CancelHotkeyCode);
            if (!cancelSuccess)
            {
                MessageBox.Show($"无法注册取消热键 {settings.CancelHotkey}，可能已被占用。");
            }
            UnregisterHotKey(this.Handle, STAMP_HOTKEY_ID);
            bool stampSuccess = RegisterHotKey(this.Handle, STAMP_HOTKEY_ID, settings.StampHotkeyModifiers, settings.StampHotkeyCode);
            if (!stampSuccess)
            {
                MessageBox.Show($"无法注册印章热键 {settings.StampHotkey}，可能已被占用。");
            }
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
            }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == STAMP_HOTKEY_ID)
            {
                StartCapture(true);
            }
            base.WndProc(ref m);
        }

        private void StartCapture(bool stampMode)
        {
            this.Hide();
            using (var captureForm = new CaptureForm(settings, stampMode))
            {
                if (captureForm.ShowDialog() == DialogResult.OK)
                {
                    var overlay = new OverlayForm(captureForm.SelectedImage, captureForm.SelectedRegion, settings);
                    overlay.Show();
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                UnregisterHotKey(this.Handle, HOTKEY_ID);
                UnregisterHotKey(this.Handle, CANCEL_HOTKEY_ID);
                UnregisterHotKey(this.Handle, STAMP_HOTKEY_ID);
                trayIcon.Dispose();
            }
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.ClientSize = new System.Drawing.Size(334, 411);
            this.Name = "MainForm";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ResumeLayout(false);
        }
    }
}
