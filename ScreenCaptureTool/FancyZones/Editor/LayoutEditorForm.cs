using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ScreenCaptureTool.FancyZones
{
    /// <summary>
    /// 区域布局编辑器主窗口：列表管理 + 预览 + 区域尺寸后下拉菜单选择指定程序 + 一键调整窗口。
    /// 窗口吸附功能仅在本页面打开期间有效。
    /// </summary>
    public sealed class LayoutEditorForm : Form
    {
        private readonly ZoneStore store;
        private readonly Settings settings;
        private readonly FancyZonesManager manager;
        private ListBox lstLayouts = null!;
        private Panel previewPanel = null!;
        private Label lblZoneAppsHeader = null!;
        private Panel zoneAppsPanel = null!;
        private Button btnSetActive = null!, btnEdit = null!, btnDuplicate = null!, btnDelete = null!, btnRename = null!;
        private Button btnNewGrid = null!, btnNewCanvas = null!, btnNewTemplate = null!;
        private Button btnApplyWindows = null!, btnClose = null!;
        private bool suppressSelectionEvent;
        private bool updatingZoneCombos;

        public ZoneLayout? CurrentLayout => SelectedLayout ?? store.ActiveLayout;

        public LayoutEditorForm(ZoneStore store, Settings settings, FancyZonesManager manager)
        {
            this.store = store;
            this.settings = settings;
            this.manager = manager;

            Text = "区域布局编辑器 (窗口吸附已启用)";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;

            BuildUI();
            RefreshList();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 仅在区域布局编辑器页面打开时启动窗口拖动吸附监测
            manager.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // 关闭区域布局编辑器页面后立即停止监测，释放系统钩子与资源
            manager.Stop();
            base.OnFormClosed(e);
        }

        private void BuildUI()
        {
            // 左侧列表
            lstLayouts = new ListBox
            {
                Location = new Point(12, 12),
                Size = new Size(260, 380),
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 28,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            lstLayouts.DrawItem += LstLayouts_DrawItem;
            lstLayouts.SelectedIndexChanged += LstLayouts_SelectedIndexChanged;
            Controls.Add(lstLayouts);

            // 左侧按钮
            int bx = 12, by = 400, bw = 82, bh = 30, gap = 4;

            btnSetActive = new Button { Text = "设为当前", Location = new Point(bx, by), Size = new Size(bw, bh), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnSetActive.Click += BtnSetActive_Click;
            Controls.Add(btnSetActive);

            btnEdit = new Button { Text = "编辑", Location = new Point(bx + (bw + gap), by), Size = new Size(60, bh), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnEdit.Click += BtnEdit_Click;
            Controls.Add(btnEdit);

            btnRename = new Button { Text = "重命名", Location = new Point(bx + (bw + gap) + 64, by), Size = new Size(60, bh), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnRename.Click += BtnRename_Click;
            Controls.Add(btnRename);

            btnDuplicate = new Button { Text = "复制", Location = new Point(bx, by + bh + gap), Size = new Size(60, bh), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnDuplicate.Click += BtnDuplicate_Click;
            Controls.Add(btnDuplicate);

            btnDelete = new Button { Text = "删除", Location = new Point(bx + 64, by + bh + gap), Size = new Size(60, bh), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnDelete.Click += BtnDelete_Click;
            Controls.Add(btnDelete);

            // 新建按钮
            btnNewGrid = new Button { Text = "+网格", Location = new Point(bx + 130, by + bh + gap), Size = new Size(60, bh), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnNewGrid.Click += (s, e) => NewLayout(LayoutKind.Grid);
            Controls.Add(btnNewGrid);

            btnNewCanvas = new Button { Text = "+画布", Location = new Point(bx + 194, by + bh + gap), Size = new Size(60, bh), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnNewCanvas.Click += (s, e) => NewLayout(LayoutKind.Canvas);
            Controls.Add(btnNewCanvas);

            btnNewTemplate = new Button { Text = "从模板...", Location = new Point(bx, by + (bh + gap) * 2), Size = new Size(120, bh), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            btnNewTemplate.Click += BtnNewTemplate_Click;
            Controls.Add(btnNewTemplate);

            // 右上预览
            previewPanel = new Panel
            {
                Location = new Point(290, 12),
                Size = new Size(416, 220),
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            previewPanel.Paint += PreviewPanel_Paint;
            Controls.Add(previewPanel);

            // 右下：各区域尺寸与指定程序下拉菜单
            lblZoneAppsHeader = new Label
            {
                Text = "区域尺寸与指定程序 (选择程序后可按快捷键或点击下方按钮一键调整窗口):",
                Location = new Point(290, 238),
                Size = new Size(416, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(lblZoneAppsHeader);

            zoneAppsPanel = new Panel
            {
                Location = new Point(290, 260),
                Size = new Size(416, 198),
                BorderStyle = BorderStyle.FixedSingle,
                AutoScroll = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(zoneAppsPanel);

            // 一键调整窗口 + 关闭
            btnApplyWindows = new Button
            {
                Text = "一键调整窗口",
                Location = new Point(456, by + (bh + gap) * 2),
                Size = new Size(134, 36),
                BackColor = Color.LightSkyBlue,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnApplyWindows.Click += (s, e) =>
            {
                var layout = SelectedLayout ?? store.ActiveLayout;
                if (layout != null)
                {
                    manager.ApplyLayoutWindows(layout, Screen.FromControl(this), this);
                }
            };
            Controls.Add(btnApplyWindows);

            btnClose = new Button
            {
                Text = "关闭",
                Location = new Point(600, by + (bh + gap) * 2),
                Size = new Size(106, 36),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
        }

        private void LstLayouts_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (suppressSelectionEvent) return;
            var layout = SelectedLayout;
            if (layout != null && store.Data.ActiveLayoutId != layout.Id)
            {
                store.SetActive(layout.Id);
                lstLayouts.Invalidate();
            }
            RefreshZoneAppList();
            previewPanel.Invalidate();
        }

        private void RefreshList()
        {
            suppressSelectionEvent = true;
            try
            {
                lstLayouts.Items.Clear();
                foreach (var layout in store.Data.Layouts)
                {
                    lstLayouts.Items.Add(layout);
                }
                var active = store.ActiveLayout;
                if (active != null)
                {
                    int idx = store.Data.Layouts.IndexOf(active);
                    if (idx >= 0) lstLayouts.SelectedIndex = idx;
                }
            }
            finally
            {
                suppressSelectionEvent = false;
            }
            RefreshZoneAppList();
            previewPanel.Invalidate();
        }

        private void RefreshZoneAppList()
        {
            zoneAppsPanel.SuspendLayout();
            try
            {
                while (zoneAppsPanel.Controls.Count > 0)
                {
                    Control c = zoneAppsPanel.Controls[0];
                    zoneAppsPanel.Controls.RemoveAt(0);
                    c.Dispose();
                }

                var layout = SelectedLayout;
                if (layout == null) return;

                layout.SyncZoneAppsCount();
                Rectangle monitorWorkArea = Screen.FromControl(this).WorkingArea;
                List<Rectangle> realZones = layout.GetZoneRects(monitorWorkArea, settings.FancyZonesSpacing);

                int y = 6;
                for (int i = 0; i < realZones.Count; i++)
                {
                    int zoneIdx = i;
                    Rectangle zr = realZones[i];
                    string currentApp = layout.GetZoneApp(i);

                    var lbl = new Label
                    {
                        Text = $"区域 {i + 1}:  {zr.Width} × {zr.Height}",
                        Location = new Point(8, y + 4),
                        Size = new Size(155, 22)
                    };

                    var cmb = new ComboBox
                    {
                        Location = new Point(168, y),
                        Size = new Size(220, 24),
                        DropDownStyle = ComboBoxStyle.DropDownList
                    };

                    PopulateProgramCombo(cmb, currentApp);

                    cmb.DropDown += (s, e) =>
                    {
                        PopulateProgramCombo(cmb, layout.GetZoneApp(zoneIdx));
                    };

                    cmb.SelectedIndexChanged += (s, e) =>
                    {
                        if (updatingZoneCombos) return;
                        string selectedProc = (cmb.SelectedItem as AppProgramItem)?.ProcessName ?? "";
                        layout.SetZoneApp(zoneIdx, selectedProc);
                        store.AddOrUpdate(layout);
                        previewPanel.Invalidate();
                    };

                    zoneAppsPanel.Controls.Add(lbl);
                    zoneAppsPanel.Controls.Add(cmb);
                    y += 30;
                }
            }
            finally
            {
                zoneAppsPanel.ResumeLayout();
            }
        }

        private void PopulateProgramCombo(ComboBox cmb, string selectedApp)
        {
            updatingZoneCombos = true;
            try
            {
                var options = FancyZonesManager.GetRunningProgramOptions(selectedApp);
                cmb.Items.Clear();
                int selIdx = 0;
                for (int i = 0; i < options.Count; i++)
                {
                    cmb.Items.Add(options[i]);
                    if (!string.IsNullOrEmpty(selectedApp) &&
                        options[i].ProcessName.Equals(selectedApp, StringComparison.OrdinalIgnoreCase))
                    {
                        selIdx = i;
                    }
                }
                if (cmb.Items.Count > 0)
                {
                    cmb.SelectedIndex = selIdx;
                }
            }
            finally
            {
                updatingZoneCombos = false;
            }
        }

        private ZoneLayout? SelectedLayout =>
            lstLayouts.SelectedItem as ZoneLayout;

        // ── 列表绘制 ──

        private void LstLayouts_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            var layout = (ZoneLayout)lstLayouts.Items[e.Index];
            bool isActive = layout.Id == store.Data.ActiveLayoutId;
            string kind = layout.Kind == LayoutKind.Grid ? "网格" : "画布";
            string text = $"{(isActive ? "★ " : "")}{layout.Name}  [{kind}, {layout.ZoneCount}区域]";

            using (SolidBrush brush = new SolidBrush(e.ForeColor))
            {
                e.Graphics.DrawString(text, e.Font!, brush, e.Bounds.X + 4, e.Bounds.Y + 4);
            }
            e.DrawFocusRectangle();
        }

        // ── 预览 ──

        private void PreviewPanel_Paint(object? sender, PaintEventArgs e)
        {
            var layout = SelectedLayout;
            if (layout == null) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.SetClip(previewPanel.ClientRectangle);

            // 16:9 预览区域
            int pw = previewPanel.ClientSize.Width - 20;
            int ph = (int)(pw * 9.0 / 16.0);
            if (ph > previewPanel.ClientSize.Height - 16)
            {
                ph = previewPanel.ClientSize.Height - 16;
                pw = (int)(ph * 16.0 / 9.0);
            }
            int px = (previewPanel.ClientSize.Width - pw) / 2;
            int py = (previewPanel.ClientSize.Height - ph) / 2;
            Rectangle area = new Rectangle(px, py, pw, ph);

            g.FillRectangle(Brushes.Black, area);

            Rectangle monitorWorkArea = Screen.FromControl(this).WorkingArea;
            double previewScale = Math.Min(pw / (double)Math.Max(1, monitorWorkArea.Width),
                ph / (double)Math.Max(1, monitorWorkArea.Height));
            int previewSpacing = (int)Math.Round(settings.FancyZonesSpacing * previewScale);
            var zones = layout.GetZoneRects(area, previewSpacing);
            var realZones = layout.GetZoneRects(monitorWorkArea, settings.FancyZonesSpacing);
            Color[] colors = {
                Color.FromArgb(100, 60, 130, 255),
                Color.FromArgb(100, 255, 160, 60),
                Color.FromArgb(100, 60, 200, 120),
                Color.FromArgb(100, 200, 60, 180),
                Color.FromArgb(100, 60, 200, 220),
                Color.FromArgb(100, 220, 220, 60)
            };

            using (Font numFont = new Font("Segoe UI", 12, FontStyle.Bold))
            using (Font infoFont = new Font("Segoe UI", 7.5f, FontStyle.Regular))
            using (StringFormat sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                for (int i = 0; i < zones.Count; i++)
                {
                    Color c = colors[i % colors.Length];
                    using (SolidBrush fb = new SolidBrush(c))
                    {
                        g.FillRectangle(fb, zones[i]);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(200, c.R, c.G, c.B), 2))
                    {
                        g.DrawRectangle(pen, zones[i]);
                    }

                    string app = layout.GetZoneApp(i);
                    string dim = i < realZones.Count ? $"{realZones[i].Width}×{realZones[i].Height}" : "";
                    string label = string.IsNullOrEmpty(app)
                        ? $"{i + 1}\n{dim}"
                        : $"{i + 1} [{app}]\n{dim}";
                    Rectangle z = zones[i];
                    RectangleF labelRect = new RectangleF(z.X, z.Y, z.Width, z.Height);
                    if (layout.Kind == LayoutKind.Canvas)
                    {
                        Rectangle vis = Rectangle.Intersect(z, area);
                        if (vis.Width > 0 && vis.Height > 0)
                        {
                            labelRect = new RectangleF(vis.X, vis.Y, vis.Width, vis.Height);
                        }
                    }
                    g.DrawString(label, infoFont, Brushes.White, labelRect, sf);
                }
            }
        }

        // ── 按钮事件 ──

        private void BtnSetActive_Click(object? sender, EventArgs e)
        {
            var layout = SelectedLayout;
            if (layout == null) return;
            store.SetActive(layout.Id);
            RefreshList();
        }

        private void BtnEdit_Click(object? sender, EventArgs e)
        {
            var layout = SelectedLayout;
            if (layout == null) return;

            var clone = layout.Clone();

            manager.SetSuspended(true);
            try
            {
                if (clone.Kind == LayoutKind.Grid)
                {
                    using (var editor = new GridEditorForm(clone, settings.FancyZonesSpacing))
                    {
                        if (editor.ShowDialog(this) == DialogResult.OK)
                        {
                            store.AddOrUpdate(editor.EditedLayout);
                            store.SetActive(editor.EditedLayout.Id);
                            RefreshList();
                        }
                    }
                }
                else
                {
                    using (var editor = new CanvasEditorForm(clone, settings.FancyZonesSpacing))
                    {
                        if (editor.ShowDialog(this) == DialogResult.OK)
                        {
                            store.AddOrUpdate(editor.EditedLayout);
                            store.SetActive(editor.EditedLayout.Id);
                            RefreshList();
                        }
                    }
                }
            }
            finally
            {
                manager.SetSuspended(false);
            }
        }

        private void BtnRename_Click(object? sender, EventArgs e)
        {
            var layout = SelectedLayout;
            if (layout == null) return;
            string? name = ShowInputBox("重命名", "输入新名称:", layout.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            layout.Name = name.Trim();
            store.AddOrUpdate(layout);
            RefreshList();
        }

        private void BtnDuplicate_Click(object? sender, EventArgs e)
        {
            var layout = SelectedLayout;
            if (layout == null) return;
            var dup = layout.Clone();
            dup.Id = Guid.NewGuid().ToString("N");
            dup.Name = layout.Name + " (副本)";
            store.AddOrUpdate(dup);
            store.SetActive(dup.Id);
            RefreshList();
        }

        private void BtnDelete_Click(object? sender, EventArgs e)
        {
            var layout = SelectedLayout;
            if (layout == null) return;
            if (!store.Remove(layout.Id))
            {
                MessageBox.Show("至少保留一个布局。");
            }
            RefreshList();
        }

        private void BtnNewTemplate_Click(object? sender, EventArgs e)
        {
            using (var dlg = new Form())
            {
                dlg.Text = "从模板创建";
                dlg.Size = new Size(300, 200);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                var lblType = new Label { Text = "模板:", Location = new Point(12, 18), AutoSize = true };
                var cmbType = new ComboBox
                {
                    Location = new Point(70, 15), Size = new Size(200, 23),
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                foreach (LayoutTemplateType t in Enum.GetValues(typeof(LayoutTemplateType)))
                {
                    cmbType.Items.Add(LayoutTemplates.GetDisplayName(t));
                }
                cmbType.SelectedIndex = 0;

                var lblCount = new Label { Text = "区域数:", Location = new Point(12, 52), AutoSize = true };
                var nudCount = new NumericUpDown
                {
                    Location = new Point(70, 49), Size = new Size(80, 23),
                    Minimum = 1, Maximum = 16, Value = 3
                };

                var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(70, 100), Size = new Size(80, 30) };
                var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(160, 100), Size = new Size(80, 30) };

                dlg.Controls.AddRange(new Control[] { lblType, cmbType, lblCount, nudCount, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var tt = (LayoutTemplateType)cmbType.SelectedIndex;
                    int n = (int)nudCount.Value;
                    var layout = LayoutTemplates.Create(tt, n);
                    store.AddOrUpdate(layout);
                    store.SetActive(layout.Id);
                    RefreshList();
                }
            }
        }

        private static string? ShowInputBox(string title, string prompt, string defaultValue)
        {
            using (var dlg = new Form())
            {
                dlg.Text = title;
                dlg.Size = new Size(340, 160);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                var lbl = new Label { Text = prompt, Location = new Point(12, 15), AutoSize = true };
                var txt = new TextBox { Text = defaultValue, Location = new Point(12, 40), Size = new Size(300, 23) };
                var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(150, 80), Size = new Size(75, 28) };
                var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(235, 80), Size = new Size(75, 28) };

                dlg.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
                dlg.AcceptButton = btnOk;
                dlg.CancelButton = btnCancel;

                return dlg.ShowDialog() == DialogResult.OK ? txt.Text : null;
            }
        }

        private void NewLayout(LayoutKind kind)
        {
            ZoneLayout layout;
            if (kind == LayoutKind.Grid)
            {
                layout = LayoutTemplates.Create(LayoutTemplateType.Columns, 2);
                layout.Id = Guid.NewGuid().ToString("N");
                layout.Name = "新网格布局";
            }
            else
            {
                layout = new ZoneLayout
                {
                    Name = "新画布布局",
                    Kind = LayoutKind.Canvas,
                    CanvasZones = new List<ZoneRect> { new ZoneRect(0.1, 0.1, 0.8, 0.8) }
                };
            }
            store.AddOrUpdate(layout);
            store.SetActive(layout.Id);
            RefreshList();

            // 直接进入编辑
            BtnEdit_Click(null, EventArgs.Empty);
        }
    }
}
