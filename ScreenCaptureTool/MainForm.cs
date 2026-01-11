using System;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ScreenCaptureTool
{
    public partial class MainForm : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private Settings settings;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        public MainForm()
        {
            InitializeComponent();
            settings = Settings.Load();
            SetupTrayIcon();
            RegisterGlobalHotKeys();
        }

        private void SetupTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("显示设置", null, (s, e) => this.Show());
            trayMenu.Items.Add("退出", null, (s, e) => Application.Exit());

            trayIcon = new NotifyIcon();
            trayIcon.Text = "屏幕截图工具";
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => this.Show();
        }

        private void RegisterGlobalHotKeys()
        {
            // 示例：F1 启动截图
            RegisterHotKey(this.Handle, 1, 0, (uint)Keys.F1);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1)
            {
                StartCapture();
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

        private void InitializeComponent()
        {
            this.Text = "屏幕截图工具设置";
            this.Size = new Size(400, 300);
            
            var lblInfo = new Label { Text = "快捷键设置 (F1 启动)", Location = new Point(20, 20), AutoSize = true };
            this.Controls.Add(lblInfo);

            var btnSave = new Button { Text = "保存设置", Location = new Point(20, 200) };
            btnSave.Click += (s, e) => settings.Save();
            this.Controls.Add(btnSave);

            this.FormClosing += (s, e) => {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    this.Hide();
                }
            };
        }
    }

    public class Settings
    {
        public float DefaultOpacity { get; set; } = 0.8f;
        public int BorderSize { get; set; } = 2;

        public static Settings Load() => new Settings();
        public void Save() { /* 保存到 JSON */ }
    }
}
