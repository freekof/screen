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

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private Label lblOpacity;
        private TrackBar trackOpacity;
        private Label lblBorder;
        private TrackBar trackBorder;
        private Label lblHotkey;
        private TextBox txtHotkey;
        private Button btnApply;
        private Button btnCapture;

        public MainForm()
        {
            InitializeComponent();
            settings = Settings.Load();
            SetupTrayIcon();
            SetupUI();
            
            this.Text = "抓屏软件设置";
            this.Size = new Size(350, 480);
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

            btnApply = new Button { Text = "保存并隐藏", Location = new Point(20, y), Size = new Size(120, 40), BackColor = Color.LightGray };
            btnApply.Click += (s, e) => {
                settings.DefaultOpacity = trackOpacity.Value;
                settings.BorderSize = trackBorder.Value;
                settings.Save();
                UpdateHotKey();
                this.Hide();
                trayIcon.ShowBalloonTip(2000, "设置已保存", $"程序已隐藏，按 {settings.Hotkey} 开始截图", ToolTipIcon.Info);
            };

            btnCapture = new Button { Text = "立即截图", Location = new Point(180, y), Size = new Size(120, 40), BackColor = Color.LightBlue };
            btnCapture.Click += (s, e) => StartCapture();

            this.Controls.Add(lblHotkey);
            this.Controls.Add(txtHotkey);
            this.Controls.Add(lblOpacity);
            this.Controls.Add(trackOpacity);
            this.Controls.Add(lblBorder);
            this.Controls.Add(trackBorder);
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
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == HOTKEY_ID)
            {
                StartCapture();
            }
            base.WndProc(ref m);
        }

        private void StartCapture()
        {
            this.Hide();
            using (var captureForm = new CaptureForm())
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
