using System;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("时间戳转换器")]
[assembly: AssemblyDescription("Unix 时间戳与地区日期时间转换工具")]
[assembly: AssemblyCompany("Local Tool")]
[assembly: AssemblyProduct("时间戳转换器")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]

internal sealed class ControllerForm : Form
{
    private static readonly Color Ink = Color.FromArgb(24, 44, 70);
    private static readonly Color Muted = Color.FromArgb(100, 115, 132);
    private static readonly Color Accent = Color.FromArgb(30, 94, 184);
    private readonly NativeNetworkIconController nativeIcon = new NativeNetworkIconController();
    private readonly NotifyIcon trayIcon;
    private readonly Icon appIcon;
    private readonly Button[] regionButtons = new Button[4];
    private readonly ToolStripMenuItem[] regionMenuItems = new ToolStripMenuItem[4];
    private readonly ComboBox unitBox;
    private readonly TextBox timestampInput, dateInput, dateOutput, timestampOutput;
    private readonly Label zoneLabel, statusLabel;
    private readonly System.Windows.Forms.Timer healthTimer;
    private ConversionRegion region = ConversionRegion.Default;
    private bool allowExit, applying, balloonShown, initializing = true;
    private bool nativeOwnsStatus, statusIsError;

    public ControllerForm()
    {
        Text = "时间戳转换器";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(680, 590);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(243, 246, 250);
        ForeColor = Ink;
        Font = new Font("Microsoft YaHei UI", 9F);
        appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
        Icon = appIcon;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 18), ColumnCount = 1, RowCount = 6 };
        foreach (int height in new[] { 62, 100, 142, 142, 58, 30 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Controls.Add(layout);
        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label { Text = "时间戳转换器", Font = new Font(Font.FontFamily, 20F, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0), ForeColor = Ink });
        header.Controls.Add(new Label { Text = "UNIX TIME  /  精确到每一毫秒", AutoSize = true, Location = new Point(2, 39), ForeColor = Muted });
        layout.Controls.Add(header, 0, 0);

        var regionPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 0, 10) };
        regionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        regionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        regionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        regionPanel.Controls.Add(new Label { Text = "地区 / 时区", AutoSize = true, ForeColor = Muted }, 0, 0);
        var regionRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            regionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var button = MakeButton(TimestampConverter.GetRegionName((ConversionRegion)i), false);
            button.Margin = new Padding(i == 0 ? 0 : 4, 0, i == 3 ? 0 : 4, 0);
            button.Click += delegate { SelectRegion((ConversionRegion)index); };
            regionButtons[i] = button;
            regionRow.Controls.Add(button, i, 0);
        }
        regionPanel.Controls.Add(regionRow, 0, 1);
        zoneLabel = new Label { Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        regionPanel.Controls.Add(zoneLabel, 0, 2);
        layout.Controls.Add(regionPanel, 0, 1);

        unitBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, AccessibleName = "时间戳单位" };
        unitBox.Items.AddRange(new object[] { "秒 (s)", "毫秒 (ms)" });
        unitBox.SelectedIndex = 0;
        timestampInput = MakeTextBox(false, "输入 Unix 时间戳");
        dateOutput = MakeTextBox(true, "日期时间结果");
        dateInput = MakeTextBox(false, "输入日期时间");
        timestampOutput = MakeTextBox(true, "时间戳结果");
        layout.Controls.Add(MakeConversionCard("时间戳 → 日期时间", timestampInput, dateOutput, unitBox, delegate { ConvertTimestamp(); }), 0, 2);
        var nowButton = MakeButton("当前时间", false);
        nowButton.Click += delegate { FillCurrentTime(); SetStatus("已填入当前时间。", false); };
        layout.Controls.Add(MakeConversionCard("日期时间 → 时间戳", dateInput, timestampOutput, nowButton, delegate { ConvertDate(); }), 0, 3);
        statusLabel = new Label { Dock = DockStyle.Fill, ForeColor = Muted, Padding = new Padding(0, 6, 0, 0), Text = "日期格式：yyyy-MM-dd HH:mm:ss（可加 .fff 毫秒）" };
        layout.Controls.Add(statusLabel, 0, 4);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        footer.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "UNIX TIME   ·   v3.0", ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var hideButton = MakeButton("收起到托盘", false);
        hideButton.Click += delegate { HideToTray(); };
        footer.Controls.Add(hideButton, 1, 0);
        layout.Controls.Add(footer, 0, 5);
        trayIcon = new NotifyIcon { Icon = appIcon, Text = "时间戳转换器 · 默认", Visible = true, ContextMenuStrip = BuildTrayMenu() };
        trayIcon.DoubleClick += delegate { ShowMainWindow(); };
        unitBox.SelectedIndexChanged += delegate
        {
            if (initializing) return;
            dateOutput.Clear(); timestampOutput.Clear();
            SetStatus("已切换单位，请重新转换。", false);
        };
        timestampInput.TextChanged += delegate { dateOutput.Clear(); };
        dateInput.TextChanged += delegate { timestampOutput.Clear(); };
        timestampInput.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { ConvertTimestamp(); e.SuppressKeyPress = true; } };
        dateInput.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { ConvertDate(); e.SuppressKeyPress = true; } };
        Resize += delegate { if (WindowState == FormWindowState.Minimized) HideToTray(); };
        healthTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        healthTimer.Tick += delegate
        {
            if (applying) return;
            string error = nativeIcon.CheckHealth();
            if (!string.IsNullOrEmpty(error))
            {
                SetStatus(error, true);
                nativeOwnsStatus = true;
            }
            else if (nativeOwnsStatus)
            {
                SetStatus("地区显示已恢复 · " + TimestampConverter.GetRegionName(region), false);
            }
        };
        healthTimer.Start();
        UpdateRegionAppearance();
        FillCurrentTime();
        initializing = false;
    }

    private static TextBox MakeTextBox(bool readOnly, string name)
    {
        return new TextBox { Dock = DockStyle.Fill, ReadOnly = readOnly, AccessibleName = name, Font = new Font("Consolas", 11F), BorderStyle = BorderStyle.FixedSingle, BackColor = readOnly ? Color.FromArgb(244, 247, 251) : Color.White, ForeColor = Ink, Margin = new Padding(0, 3, 8, 3) };
    }

    private static Button MakeButton(string text, bool primary)
    {
        var button = new Button { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand, Margin = Padding.Empty, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(211, 220, 230);
        return button;
    }

    private Control MakeConversionCard(string title, TextBox input, TextBox output, Control accessory, Action convert)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 9, 14, 9), Margin = new Padding(0, 0, 0, 12), ColumnCount = 3, RowCount = 3 };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        var caption = new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = Ink };
        card.Controls.Add(caption, 0, 0); card.SetColumnSpan(caption, 3);
        card.Controls.Add(input, 0, 1);
        accessory.Margin = new Padding(0, 3, 8, 3);
        card.Controls.Add(accessory, 1, 1);
        var convertButton = MakeButton("转换", true);
        convertButton.Margin = new Padding(0, 3, 0, 3);
        convertButton.Click += delegate { convert(); };
        card.Controls.Add(convertButton, 2, 1);
        card.Controls.Add(output, 0, 2); card.SetColumnSpan(output, 2);
        var copyButton = MakeButton("复制", false);
        copyButton.Margin = new Padding(0, 3, 0, 3);
        copyButton.Click += delegate
        {
            if (string.IsNullOrEmpty(output.Text)) { SetStatus("请先转换，再复制结果。", true); return; }
            try { Clipboard.SetText(output.Text); SetStatus("结果已复制。", false); }
            catch (ExternalException) { SetStatus("剪贴板正被占用，请重试。", true); }
        };
        card.Controls.Add(copyButton, 2, 2);
        return card;
    }

    private bool Milliseconds { get { return unitBox.SelectedIndex == 1; } }

    private void ConvertTimestamp()
    {
        try
        {
            DateTimeOffset result = TimestampConverter.FromTimestamp(timestampInput.Text, Milliseconds, region);
            dateOutput.Text = result.ToString(Milliseconds ? "yyyy-MM-dd HH:mm:ss.fff zzz" : "yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            SetStatus("转换完成 · " + TimestampConverter.GetTimeZoneDescription(region), false);
        }
        catch (Exception ex) { dateOutput.Clear(); SetStatus(ex.Message, true); }
    }

    private void ConvertDate()
    {
        try
        {
            timestampOutput.Text = TimestampConverter.ToTimestamp(dateInput.Text, Milliseconds, region).ToString(CultureInfo.InvariantCulture);
            SetStatus("转换完成 · Unix 时间戳（" + (Milliseconds ? "毫秒" : "秒") + "）", false);
        }
        catch (Exception ex) { timestampOutput.Clear(); SetStatus(ex.Message, true); }
    }

    private void FillCurrentTime()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        long ticks = now.UtcDateTime.Ticks - 621355968000000000L;
        timestampInput.Text = (ticks / (Milliseconds ? TimeSpan.TicksPerMillisecond : TimeSpan.TicksPerSecond)).ToString(CultureInfo.InvariantCulture);
        dateInput.Text = TimeZoneInfo.ConvertTime(now, TimestampConverter.GetTimeZone(region)).ToString(Milliseconds ? "yyyy-MM-dd HH:mm:ss.fff" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private void SelectRegion(ConversionRegion value)
    {
        if (applying) return;
        applying = true; UseWaitCursor = true;
        foreach (Button button in regionButtons) button.Enabled = false;
        foreach (ToolStripMenuItem item in regionMenuItems) item.Enabled = false;
        SetStatus("正在应用地区设置…", false);
        ThreadPool.QueueUserWorkItem(delegate
        {
            string error;
            bool success = nativeIcon.TryApply(value, out error);
            try
            {
                BeginInvoke((Action)delegate
                {
                    applying = false; UseWaitCursor = false;
                    foreach (Button button in regionButtons) button.Enabled = true;
                    foreach (ToolStripMenuItem item in regionMenuItems) item.Enabled = true;
                    region = success ? value : ConversionRegion.Default;
                    UpdateRegionAppearance();
                    dateOutput.Clear(); timestampOutput.Clear();
                    if (success) SetStatus("已选择" + TimestampConverter.GetRegionName(region) + "。输入内容保留，请重新转换。", false);
                    else { SetStatus(error, true); ShowMainWindow(); }
                });
            }
            catch (InvalidOperationException) { /* Form already closed during session shutdown. */ }
        });
    }

    private void UpdateRegionAppearance()
    {
        for (int i = 0; i < regionButtons.Length; i++)
        {
            bool selected = i == (int)region;
            regionButtons[i].BackColor = selected ? Accent : Color.White;
            regionButtons[i].ForeColor = selected ? Color.White : Ink;
            regionButtons[i].AccessibleDescription = selected ? "已选择" : "未选择";
            if (regionMenuItems[i] != null) regionMenuItems[i].Checked = selected;
        }
        zoneLabel.Text = TimestampConverter.GetTimeZoneDescription(region);
        if (trayIcon != null) trayIcon.Text = "时间戳转换器 · " + TimestampConverter.GetRegionName(region);
    }

    private void SetStatus(string message, bool error)
    {
        nativeOwnsStatus = false;
        statusIsError = error;
        statusLabel.Text = message;
        statusLabel.ForeColor = error ? Color.FromArgb(171, 50, 50) : Muted;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开时间戳转换器", null, delegate { ShowMainWindow(); });
        menu.Items.Add(new ToolStripSeparator());
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(TimestampConverter.GetRegionName((ConversionRegion)i));
            item.Click += delegate { SelectRegion((ConversionRegion)index); if (statusIsError) ShowMainWindow(); };
            regionMenuItems[i] = item; menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        // Exit must remain available even if Explorer's diagnostics attach hangs.
        menu.Items.Add("退出", null, delegate { allowExit = true; Close(); });
        return menu;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == ControllerApp.ShowWindowMessage) ShowMainWindow();
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); return; }
        nativeIcon.Dispose(); trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (healthTimer != null) healthTimer.Dispose();
            nativeIcon.Dispose();
            if (trayIcon != null) { if (trayIcon.ContextMenuStrip != null) trayIcon.ContextMenuStrip.Dispose(); trayIcon.Dispose(); }
            if (appIcon != null) appIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private void HideToTray()
    {
        Hide(); ShowInTaskbar = false;
        if (!balloonShown) { trayIcon.ShowBalloonTip(2000, "时间戳转换器", "双击时钟图标可重新打开窗口。", ToolTipIcon.Info); balloonShown = true; }
    }

    private void ShowMainWindow()
    {
        ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate();
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int RegisterWindowMessage(string messageName);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}

internal static class ControllerApp
{
    public static readonly int ShowWindowMessage = NativeMethods.RegisterWindowMessage("TimestampConverter_ShowWindow_v3");
    [STAThread]
    public static void Main()
    {
        bool createdNew;
        using (var mutex = new Mutex(true, @"Local\TimestampConverter_v3", out createdNew))
        {
            if (!createdNew) { NativeMethods.PostMessage(new IntPtr(0xffff), ShowWindowMessage, IntPtr.Zero, IntPtr.Zero); return; }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ControllerForm());
            GC.KeepAlive(mutex);
        }
    }
}
