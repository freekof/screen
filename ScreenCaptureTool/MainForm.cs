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

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private Label lblOpacity;
        private TrackBar trackOpacity;
        private Label lblBorder;
        private TrackBar trackBorder;
        private Label lblSimilarity;
        private TrackBar trackSimilarity;
        private Label lblMarkerBorder;
        private TrackBar trackMarkerBorder;
        private Label lblMarkerFont;
        private TrackBar trackMarkerFont;
        private Label lblMarkerAlpha;
        private TrackBar trackMarkerAlpha;
        private Label lblMagnifierSize;
        private TrackBar trackMagnifierSize;
        private Label lblMagnifierZoom;
        private TrackBar trackMagnifierZoom;
        private Label lblMagnifierFont;
        private TrackBar trackMagnifierFont;
        private CheckBox chkStampMode;
        private Label lblStampWidth;
        private TrackBar trackStampWidth;
        private Label lblStampHeight;
        private TrackBar trackStampHeight;
        private Label lblStampStep;
        private TrackBar trackStampStep;
        private Label lblHotkey;
        private TextBox txtHotkey;
        private Label lblCancelHotkey;
        private TextBox txtCancelHotkey;
        private Button btnApply;
        private Button btnCapture;

        public MainForm()
        {
            InitializeComponent();
            settings = Settings.Load();
            SetupTrayIcon();
            SetupUI();
            
            this.Text = "抓屏软件设置";
            this.Size = new Size(360, 820);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
        }

        private void SetupUI()
        {
            int y = 20;
            
            lblHotkey = new Label { Text = "启动快捷键 (点击下方框后按键):", Location = new Point(20, y), Size = new Size(280, 20) };
            y += 25;
            txtHotkey = new TextBox { 
                Text = settings.Hotkey, 
                Location = new Point(20, y), 
                Size = new Size(280, 25), 
                ReadOnly = true,
                BackColor = Color.White,
                TextAlign = HorizontalAlignment.Center
            };
            txtHotkey.KeyDown += TxtHotkey_KeyDown;
            y += 40;

            lblCancelHotkey = new Label { Text = "取消快捷键 (点击下方框后按键):", Location = new Point(20, y), Size = new Size(280, 20) };
            y += 25;
            txtCancelHotkey = new TextBox {
                Text = settings.CancelHotkey,
                Location = new Point(20, y),
                Size = new Size(280, 25),
                ReadOnly = true,
                BackColor = Color.White,
                TextAlign = HorizontalAlignment.Center
            };
            txtCancelHotkey.KeyDown += TxtCancelHotkey_KeyDown;
            y += 40;

            lblOpacity = new Label { Text = $"初始透明度: {settings.DefaultOpacity}%", Location = new Point(20, y), Size = new Size(200, 20) };
            y += 25;
            trackOpacity = new TrackBar { Minimum = 10, Maximum = 100, Value = settings.DefaultOpacity, Location = new Point(20, y), Size = new Size(280, 45) };
            trackOpacity.Scroll += (s, e) => lblOpacity.Text = $"初始透明度: {trackOpacity.Value}%";
            y += 50;

            lblBorder = new Label { Text = $"边框大小: {settings.BorderSize}px", Location = new Point(20, y), Size = new Size(200, 20) };
            y += 25;
            trackBorder = new TrackBar { Minimum = 0, Maximum = 20, Value = settings.BorderSize, Location = new Point(20, y), Size = new Size(280, 45) };
            trackBorder.Scroll += (s, e) => lblBorder.Text = $"边框大小: {trackBorder.Value}px";
            y += 60;

            lblSimilarity = new Label { Text = $"相似度: {settings.SimilarityThresholdPercent}%", Location = new Point(20, y), Size = new Size(200, 20) };
            y += 25;
            trackSimilarity = new TrackBar { Minimum = 50, Maximum = 99, Value = settings.SimilarityThresholdPercent, Location = new Point(20, y), Size = new Size(280, 45) };
            trackSimilarity.Scroll += (s, e) => lblSimilarity.Text = $"相似度: {trackSimilarity.Value}%";
            y += 60;

            lblMarkerBorder = new Label { Text = $"标记框粗细: {settings.MarkerBorderThickness}px", Location = new Point(20, y), Size = new Size(220, 20) };
            y += 25;
            trackMarkerBorder = new TrackBar { Minimum = 1, Maximum = 10, Value = settings.MarkerBorderThickness, Location = new Point(20, y), Size = new Size(280, 45) };
            trackMarkerBorder.Scroll += (s, e) => lblMarkerBorder.Text = $"标记框粗细: {trackMarkerBorder.Value}px";
            y += 50;

            lblMarkerFont = new Label { Text = $"标记文字大小: {settings.MarkerFontSize}", Location = new Point(20, y), Size = new Size(220, 20) };
            y += 25;
            trackMarkerFont = new TrackBar { Minimum = 10, Maximum = 48, Value = settings.MarkerFontSize, Location = new Point(20, y), Size = new Size(280, 45) };
            trackMarkerFont.Scroll += (s, e) => lblMarkerFont.Text = $"标记文字大小: {trackMarkerFont.Value}";
            y += 50;

            lblMarkerAlpha = new Label { Text = $"标记框透明度: {settings.MarkerFillAlpha}", Location = new Point(20, y), Size = new Size(220, 20) };
            y += 25;
            trackMarkerAlpha = new TrackBar { Minimum = 20, Maximum = 200, Value = settings.MarkerFillAlpha, Location = new Point(20, y), Size = new Size(280, 45) };
            trackMarkerAlpha.Scroll += (s, e) => lblMarkerAlpha.Text = $"标记框透明度: {trackMarkerAlpha.Value}";
            y += 55;

            lblMagnifierSize = new Label { Text = $"放大镜大小: {settings.MagnifierSize}px", Location = new Point(20, y), Size = new Size(220, 20) };
            y += 25;
            trackMagnifierSize = new TrackBar { Minimum = 80, Maximum = 260, Value = settings.MagnifierSize, Location = new Point(20, y), Size = new Size(280, 45) };
            trackMagnifierSize.Scroll += (s, e) => lblMagnifierSize.Text = $"放大镜大小: {trackMagnifierSize.Value}px";
            y += 50;

            lblMagnifierZoom = new Label { Text = $"放大倍数: {settings.MagnifierZoom}x", Location = new Point(20, y), Size = new Size(220, 20) };
            y += 25;
            trackMagnifierZoom = new TrackBar { Minimum = 2, Maximum = 15, Value = settings.MagnifierZoom, Location = new Point(20, y), Size = new Size(280, 45) };
            trackMagnifierZoom.Scroll += (s, e) => lblMagnifierZoom.Text = $"放大倍数: {trackMagnifierZoom.Value}x";
            y += 50;

            lblMagnifierFont = new Label { Text = $"放大镜文字大小: {settings.MagnifierFontSize}", Location = new Point(20, y), Size = new Size(240, 20) };
            y += 25;
            trackMagnifierFont = new TrackBar { Minimum = 8, Maximum = 20, Value = settings.MagnifierFontSize, Location = new Point(20, y), Size = new Size(280, 45) };
            trackMagnifierFont.Scroll += (s, e) => lblMagnifierFont.Text = $"放大镜文字大小: {trackMagnifierFont.Value}";
            y += 55;

            chkStampMode = new CheckBox { Text = "启用印章模式", Location = new Point(20, y), Size = new Size(200, 24), Checked = settings.StampModeEnabled };
            y += 30;

            lblStampWidth = new Label { Text = $"印章框宽度: {settings.StampBoxWidth}px", Location = new Point(20, y), Size = new Size(220, 20) };
            y += 25;
            trackStampWidth = new TrackBar { Minimum = 40, Maximum = 400, Value = settings.StampBoxWidth, Location = new Point(20, y), Size = new Size(280, 45) };
            trackStampWidth.Scroll += (s, e) => lblStampWidth.Text = $"印章框宽度: {trackStampWidth.Value}px";
            y += 50;

            lblStampHeight = new Label { Text = $"印章框高度: {settings.StampBoxHeight}px", Location = new Point(20, y), Size = new Size(220, 20) };
            y += 25;
            trackStampHeight = new TrackBar { Minimum = 40, Maximum = 400, Value = settings.StampBoxHeight, Location = new Point(20, y), Size = new Size(280, 45) };
            trackStampHeight.Scroll += (s, e) => lblStampHeight.Text = $"印章框高度: {trackStampHeight.Value}px";
            y += 50;

            lblStampStep = new Label { Text = $"滚轮缩放步进: {settings.StampWheelScaleStepPercent}%", Location = new Point(20, y), Size = new Size(240, 20) };
            y += 25;
            trackStampStep = new TrackBar { Minimum = 5, Maximum = 30, Value = settings.StampWheelScaleStepPercent, Location = new Point(20, y), Size = new Size(280, 45) };
            trackStampStep.Scroll += (s, e) => lblStampStep.Text = $"滚轮缩放步进: {trackStampStep.Value}%";
            y += 60;

            btnApply = new Button { Text = "保存并隐藏", Location = new Point(20, y), Size = new Size(120, 40), BackColor = Color.LightGray };
            btnApply.Click += (s, e) => {
                settings.DefaultOpacity = trackOpacity.Value;
                settings.BorderSize = trackBorder.Value;
                settings.SimilarityThresholdPercent = trackSimilarity.Value;
                settings.MarkerBorderThickness = trackMarkerBorder.Value;
                settings.MarkerFontSize = trackMarkerFont.Value;
                settings.MarkerFillAlpha = trackMarkerAlpha.Value;
                settings.MagnifierSize = trackMagnifierSize.Value;
                settings.MagnifierZoom = trackMagnifierZoom.Value;
                settings.MagnifierFontSize = trackMagnifierFont.Value;
                settings.StampModeEnabled = chkStampMode.Checked;
                settings.StampBoxWidth = trackStampWidth.Value;
                settings.StampBoxHeight = trackStampHeight.Value;
                settings.StampWheelScaleStepPercent = trackStampStep.Value;
                settings.Save();
                UpdateHotKey();
                this.Hide();
                trayIcon.ShowBalloonTip(2000, "设置已保存", $"程序已隐藏，按 {settings.Hotkey} 开始截图", ToolTipIcon.Info);
            };

            btnCapture = new Button { Text = "立即截图", Location = new Point(180, y), Size = new Size(120, 40), BackColor = Color.LightBlue };
            btnCapture.Click += (s, e) => StartCapture();

            this.Controls.Add(lblHotkey);
            this.Controls.Add(txtHotkey);
            this.Controls.Add(lblCancelHotkey);
            this.Controls.Add(txtCancelHotkey);
            this.Controls.Add(lblOpacity);
            this.Controls.Add(trackOpacity);
            this.Controls.Add(lblBorder);
            this.Controls.Add(trackBorder);
            this.Controls.Add(lblSimilarity);
            this.Controls.Add(trackSimilarity);
            this.Controls.Add(lblMarkerBorder);
            this.Controls.Add(trackMarkerBorder);
            this.Controls.Add(lblMarkerFont);
            this.Controls.Add(trackMarkerFont);
            this.Controls.Add(lblMarkerAlpha);
            this.Controls.Add(trackMarkerAlpha);
            this.Controls.Add(lblMagnifierSize);
            this.Controls.Add(trackMagnifierSize);
            this.Controls.Add(lblMagnifierZoom);
            this.Controls.Add(trackMagnifierZoom);
            this.Controls.Add(lblMagnifierFont);
            this.Controls.Add(trackMagnifierFont);
            this.Controls.Add(chkStampMode);
            this.Controls.Add(lblStampWidth);
            this.Controls.Add(trackStampWidth);
            this.Controls.Add(lblStampHeight);
            this.Controls.Add(trackStampHeight);
            this.Controls.Add(lblStampStep);
            this.Controls.Add(trackStampStep);
            this.Controls.Add(btnApply);
            this.Controls.Add(btnCapture);
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

        private void SetupTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示设置", null, (s, e) => this.ShowAndActivate());
            trayMenu.Items.Add("立即截图 (F1)", null, (s, e) => StartCapture());
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
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HOTKEY_ID)
            {
                StartCapture();
            }
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == CANCEL_HOTKEY_ID)
            {
                OverlayForm.CloseAllOpen();
            }
            base.WndProc(ref m);
        }

        private void StartCapture()
        {
            this.Hide();
            using (var captureForm = new CaptureForm(settings))
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
