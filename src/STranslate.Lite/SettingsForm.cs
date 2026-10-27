using System.Drawing;
using System.Windows.Forms;
using STranslate.Lite.Engines;

namespace STranslate.Lite;

/// <summary>
/// 常规设置窗口。只有这个窗口会主动获取焦点，翻译浮窗始终不会。
/// 布局使用单层 TableLayoutPanel，避免嵌套 AutoSize 造成的行高测量偏差。
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly Color OkColor = Color.FromArgb(0, 128, 64);
    private static readonly Color FailColor = Color.FromArgb(176, 0, 32);

    private readonly Func<AppSettings, string?> _saveHandler;
    private readonly Dictionary<TranslatorKind, ServiceOptions> _services;
    private readonly TableLayoutPanel _table;

    private readonly ComboBox _triggerCombo;
    private readonly ComboBox _targetCombo;
    private readonly ComboBox _engineCombo;
    private readonly TextBox _hotkeyBox;
    private readonly TextBox _endpointBox;
    private readonly TextBox _apiKeyBox;
    private readonly TextBox _modelBox;
    private readonly CheckBox _fullscreenCheckBox;
    private readonly CheckBox _autoStartCheckBox;
    private readonly Label _hotkeyHint;
    private readonly Label _serviceHint;
    private readonly Label _testResultLabel;
    private readonly Button _testButton;
    private readonly Label _serviceHeader;
    private readonly Label _endpointLabel;
    private readonly Label _apiKeyLabel;
    private readonly Label _modelLabel;

    private HotkeyDefinition _hotkey;
    private bool _capturingHotkey;

    public SettingsForm(AppSettings settings, Func<AppSettings, string?> saveHandler)
    {
        _saveHandler = saveHandler;
        _hotkey = settings.Hotkey.Clone();
        _services = new Dictionary<TranslatorKind, ServiceOptions>();
        foreach (var engine in EngineRegistry.All)
            _services[engine.Kind] = settings.GetService(engine.Kind).Clone();

        Text = "STranslate Lite 设置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(600, 745);
        Padding = new Padding(10);

        _table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 2,
            Padding = new Padding(2)
        };
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _triggerCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _triggerCombo.Items.AddRange(
        [
            new ChoiceItem<TriggerMode>("鼠标侧键 XButton1（后退键）", TriggerMode.XButton1),
            new ChoiceItem<TriggerMode>("鼠标侧键 XButton2（前进键）", TriggerMode.XButton2),
            new ChoiceItem<TriggerMode>("键盘组合键", TriggerMode.Hotkey),
            new ChoiceItem<TriggerMode>("禁用", TriggerMode.Disabled)
        ]);
        SelectChoice(_triggerCombo, settings.TriggerMode);

        _hotkeyBox = new TextBox
        {
            ReadOnly = true,
            ShortcutsEnabled = false,
            TextAlign = HorizontalAlignment.Center,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
            Cursor = Cursors.Hand
        };
        _hotkeyBox.KeyDown += OnHotkeyKeyDown;
        _hotkeyBox.Enter += OnHotkeyEnter;
        _hotkeyBox.Leave += OnHotkeyLeave;
        UpdateHotkeyText();

        _hotkeyHint = CreateHint(
            "点击后按组合键，需含 Ctrl / Alt / Shift / Win。");

        _engineCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        foreach (var engine in EngineRegistry.All)
            _engineCombo.Items.Add(new ChoiceItem<TranslatorKind>($"{engine.Name} · {engine.Group}", engine.Kind));
        SelectChoice(_engineCombo, settings.Translator);
        _engineCombo.SelectedIndexChanged += (_, _) => OnEngineChanged();

        _targetCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _targetCombo.Items.AddRange(
        [
            new ChoiceItem<string>("自动（中文→英文，其它→中文）", "auto"),
            new ChoiceItem<string>("简体中文", "zh-CN"),
            new ChoiceItem<string>("繁體中文", "zh-TW"),
            new ChoiceItem<string>("English", "en"),
            new ChoiceItem<string>("日本語", "ja"),
            new ChoiceItem<string>("한국어", "ko"),
            new ChoiceItem<string>("Français", "fr"),
            new ChoiceItem<string>("Deutsch", "de"),
            new ChoiceItem<string>("Español", "es"),
            new ChoiceItem<string>("Русский", "ru"),
            new ChoiceItem<string>("Português", "pt"),
            new ChoiceItem<string>("Italiano", "it"),
            new ChoiceItem<string>("العربية", "ar"),
            new ChoiceItem<string>("Tiếng Việt", "vi"),
            new ChoiceItem<string>("ไทย", "th"),
            new ChoiceItem<string>("Bahasa Indonesia", "id"),
            new ChoiceItem<string>("Türkçe", "tr"),
            new ChoiceItem<string>("हिन्दी", "hi"),
            new ChoiceItem<string>("Nederlands", "nl"),
            new ChoiceItem<string>("Polski", "pl"),
            new ChoiceItem<string>("Українська", "uk"),
            new ChoiceItem<string>("فارسی", "fa")
        ]);
        SelectChoice(_targetCombo, settings.TargetLanguage);

        _endpointBox = CreateTextBox();
        _apiKeyBox = CreateTextBox();
        _apiKeyBox.UseSystemPasswordChar = true;
        _modelBox = CreateTextBox();
        _serviceHint = CreateHint(string.Empty);

        _testButton = new Button
        {
            Text = "测试当前引擎",
            AutoSize = true,
            MinimumSize = new Size(110, 30)
        };
        _testButton.Click += OnTestClicked;

        _testResultLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(340, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(10, 9, 3, 3),
            Text = "固定英文测试句，验证网络与配置。"
        };

        _fullscreenCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "前台应用全屏时禁用翻译（不拦截侧键）",
            Checked = settings.IgnoreFullscreen
        };

        _autoStartCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "开机自动启动（当前用户，无需管理员权限）",
            Checked = settings.AutoStart
        };

        AddSection("触发方式");
        AddFieldRow("触发方式", _triggerCombo);
        AddFieldRow("键盘快捷键", _hotkeyBox);
        AddSpan(_hotkeyHint);
        AddSpan(CreateHint("侧键会拦截原先前/后退行为；全屏应用下自动放行。"));

        AddSection("翻译");
        AddFieldRow("翻译引擎", _engineCombo);
        AddFieldRow("目标语言", _targetCombo);
        AddSpan(CreateHint(
            "15 个免密钥引擎，另有本地 / 自建服务与大模型接口。"));

        _serviceHeader = AddSection("引擎配置");
        _endpointLabel = AddFieldRow("服务地址", _endpointBox);
        _apiKeyLabel = AddFieldRow("API Key", _apiKeyBox);
        _modelLabel = AddFieldRow("模型", _modelBox);
        AddSpan(_serviceHint);

        AddSection("引擎测试");
        AddSpan(CreateTestPanel());

        _fullscreenCheckBox.Margin = new Padding(3, 10, 3, 4);
        AddCheck(_fullscreenCheckBox);
        AddCheck(_autoStartCheckBox);
        AddFillerRow();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_table, 0, 0);
        root.Controls.Add(CreateFooter(), 0, 1);

        Controls.Add(root);
        LoadServiceIntoEditors(settings.Translator);
        UpdateEngineState();

        // 运行时构造的窗体不会自动带上设计器的 AutoScaleDimensions。
        // 必须在控件树和 ClientSize 都就绪后再开启 Dpi 缩放，否则 WinForms 会
        // 先用默认尺寸做一次缩放，随后写入的 ClientSize 又变成未缩放的像素值。
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        PerformAutoScale();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // 150%/200% 下按逻辑尺寸放大后可能超出工作区，允许内部表格滚动，
        // 但外框必须收进当前显示器，避免标题栏或底部按钮跑到屏幕外。
        ClampToWorkingArea();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        // 基类会在本轮消息处理中按新 DPI 重排控件，延后一帧再限制外框尺寸。
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(new Action(ClampToWorkingArea));
    }

    private void ClampToWorkingArea()
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        var workingArea = Screen.FromControl(this).WorkingArea;
        var margin = Math.Max(16, DeviceDpi / 6);
        var maxWidth = Math.Max(320, workingArea.Width - margin * 2);
        var maxHeight = Math.Max(240, workingArea.Height - margin * 2);

        var width = Math.Min(Width, maxWidth);
        var height = Math.Min(Height, maxHeight);
        if (width == Width && height == Height)
            return;

        Size = new Size(width, height);
        Location = new Point(
            workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2));
    }

    private Label AddSection(string title)
    {
        var label = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size + 0.5f, FontStyle.Bold),
            ForeColor = SystemColors.HotTrack,
            Margin = new Padding(3, 4, 3, 2)
        };

        var row = NewRow();
        _table.Controls.Add(label, 0, row);
        _table.SetColumnSpan(label, 2);
        return label;
    }

    private Label AddFieldRow(string label, Control control)
    {
        var row = NewRow();
        var text = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 3, 3)
        };

        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 4, 3, 4);
        _table.Controls.Add(text, 0, row);
        _table.Controls.Add(control, 1, row);
        return text;
    }

    private void AddSpan(Control control)
    {
        var row = NewRow();
        control.Dock = DockStyle.Fill;
        _table.Controls.Add(control, 0, row);
        _table.SetColumnSpan(control, 2);
    }

    /// <summary>
    /// 复选框用 Anchor 而不是 Dock：Dock + AutoSize 会让所在行被撑高，导致下方留出大块空白。
    /// </summary>
    private void AddCheck(CheckBox checkBox)
    {
        var row = NewRow();
        checkBox.Anchor = AnchorStyles.Left;
        checkBox.Margin = new Padding(3, 4, 3, 4);
        _table.Controls.Add(checkBox, 0, row);
        _table.SetColumnSpan(checkBox, 2);
    }

    private Control CreateFooter()
    {
        var saveButton = new Button
        {
            Text = "保存",
            AutoSize = true,
            MinimumSize = new Size(92, 32)
        };
        saveButton.Click += OnSaveClicked;

        var cancelButton = new Button
        {
            Text = "取消",
            AutoSize = true,
            MinimumSize = new Size(92, 32),
            DialogResult = DialogResult.Cancel
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(saveButton);

        var pathLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 20,
            AutoEllipsis = true,
            ForeColor = SystemColors.GrayText,
            Text = $"配置文件：{AppPaths.SettingsFile}"
        };

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(2, 6, 2, 2)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.Controls.Add(buttons, 0, 0);
        footer.Controls.Add(pathLabel, 0, 1);
        return footer;
    }

    private Control CreateTestPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(3, 2, 3, 2)
        };
        panel.Controls.Add(_testButton);
        panel.Controls.Add(_testResultLabel);
        return panel;
    }

    private int NewRow()
    {
        var index = _table.RowStyles.Count;
        _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _table.RowCount = index + 1;
        return index;
    }

    /// <summary>
    /// 末尾留一行百分比空白，吸收窗口多余高度。
    /// 否则 TableLayoutPanel 会把剩余高度加到最后一行，复选框之间出现大段空白。
    /// </summary>
    private void AddFillerRow()
    {
        _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _table.RowCount = _table.RowStyles.Count;
    }

    private static Label CreateHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(540, 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(3, 2, 3, 4)
    };

    private static TextBox CreateTextBox() => new()
    {
        Dock = DockStyle.Fill
    };

    private static void SelectChoice<T>(ComboBox comboBox, T value)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is ChoiceItem<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }

    private static T GetChoice<T>(ComboBox comboBox, T fallback)
    {
        return comboBox.SelectedItem is ChoiceItem<T> choice ? choice.Value : fallback;
    }

    private void OnEngineChanged()
    {
        CommitServiceEdits();
        var kind = GetChoice(_engineCombo, TranslatorKind.Google);
        LoadServiceIntoEditors(kind);
        UpdateEngineState();
    }

    private void CommitServiceEdits()
    {
        var kind = GetChoice(_engineCombo, TranslatorKind.Google);
        var service = _services[kind];
        service.Endpoint = _endpointBox.Text.Trim();
        service.ApiKey = _apiKeyBox.Text.Trim();
        service.Model = _modelBox.Text.Trim();
    }

    private void LoadServiceIntoEditors(TranslatorKind kind)
    {
        var engine = EngineRegistry.Get(kind);
        var service = _services[kind];
        _endpointBox.Text = string.IsNullOrWhiteSpace(service.Endpoint)
            ? engine.DefaultEndpoint ?? string.Empty
            : service.Endpoint;
        _apiKeyBox.Text = service.ApiKey;
        _modelBox.Text = string.IsNullOrWhiteSpace(service.Model)
            ? engine.DefaultModel ?? string.Empty
            : service.Model;

        _testResultLabel.ForeColor = SystemColors.GrayText;
        _testResultLabel.Text = "固定英文测试句，验证网络与配置。";
    }

    private void UpdateEngineState()
    {
        var kind = GetChoice(_engineCombo, TranslatorKind.Google);
        var engine = EngineRegistry.Get(kind);

        var showEndpoint = engine.SupportsEndpoint;
        var showApiKey = engine.RequiresApiKey || kind == TranslatorKind.LibreTranslate;
        var showModel = engine.SupportsModel;
        var showService = showEndpoint || showApiKey || showModel;

        _endpointBox.Visible = showEndpoint;
        _endpointLabel.Visible = showEndpoint;
        _apiKeyBox.Visible = showApiKey;
        _apiKeyLabel.Visible = showApiKey;
        _modelBox.Visible = showModel;
        _modelLabel.Visible = showModel;
        _serviceHeader.Visible = showService;

        // 地址框里已经显示了默认地址，提示里不再重复，避免窗口内出现冗余的换行。
        var hint = engine.ConfigHint ?? string.Empty;
        _serviceHint.Text = hint;
        _serviceHint.Visible = showService && !string.IsNullOrWhiteSpace(hint);
    }

    private void OnHotkeyEnter(object? sender, EventArgs e)
    {
        _capturingHotkey = true;
        _hotkeyBox.Text = "请按下组合键…";
    }

    private void OnHotkeyLeave(object? sender, EventArgs e)
    {
        _capturingHotkey = false;
        UpdateHotkeyText();
    }

    private void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        if (!_capturingHotkey)
            return;

        if (e.KeyCode is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
            Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
            Keys.Menu or Keys.LMenu or Keys.RMenu or
            Keys.LWin or Keys.RWin or Keys.None)
        {
            return;
        }

        var ctrl = e.Control;
        var alt = e.Alt;
        var shift = e.Shift;
        var win = IsKeyDown(NativeMethods.VK_LWIN) || IsKeyDown(NativeMethods.VK_RWIN);

        if (e.KeyCode == Keys.Escape && !ctrl && !alt && !shift && !win)
        {
            UpdateHotkeyText();
            return;
        }

        if (!ctrl && !alt && !shift && !win)
        {
            _hotkeyBox.Text = "需要 Ctrl / Alt / Shift / Win";
            return;
        }

        _hotkey = new HotkeyDefinition
        {
            Ctrl = ctrl,
            Alt = alt,
            Shift = shift,
            Win = win,
            VirtualKey = (int)e.KeyCode
        };
        _hotkeyBox.Text = _hotkey.Display;
    }

    private void UpdateHotkeyText()
    {
        _hotkeyBox.Text = _hotkey.IsValid ? _hotkey.Display : "未设置";
    }

    private async void OnTestClicked(object? sender, EventArgs e)
    {
        CommitServiceEdits();

        var kind = GetChoice(_engineCombo, TranslatorKind.Google);
        var engine = EngineRegistry.Get(kind);
        if (engine.RequiresApiKey && string.IsNullOrWhiteSpace(_apiKeyBox.Text))
        {
            ShowTestResult($"{engine.Name} 需要先填写 API Key。", fail: true);
            return;
        }

        var target = GetChoice(_targetCombo, "auto");
        if (string.Equals(target, "auto", StringComparison.OrdinalIgnoreCase))
            target = "zh-CN";

        _testButton.Enabled = false;
        ShowTestResult("正在测试…", fail: false);

        try
        {
            var result = await TranslationService.TestAsync(
                kind,
                _services[kind],
                target,
                "Hello, world! This is a translation test.",
                CancellationToken.None);

            if (!IsDisposed)
                ShowTestResult($"测试通过（{result.Text}）", fail: false);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                ShowTestResult($"测试失败：{ex.Message}", fail: true);
        }
        finally
        {
            if (!IsDisposed)
                _testButton.Enabled = true;
        }
    }

    private void ShowTestResult(string text, bool fail)
    {
        _testResultLabel.ForeColor = fail ? FailColor : OkColor;
        _testResultLabel.Text = text;
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        var triggerMode = GetChoice(_triggerCombo, TriggerMode.XButton1);
        if (triggerMode == TriggerMode.Hotkey && !_hotkey.IsValid)
        {
            AppMessageBox.ShowError("请先录制一个包含修饰键的快捷键。", "快捷键无效", this);
            return;
        }

        CommitServiceEdits();

        var kind = GetChoice(_engineCombo, TranslatorKind.Google);
        var engine = EngineRegistry.Get(kind);
        var service = _services[kind];

        if (engine.SupportsEndpoint &&
            !string.IsNullOrWhiteSpace(service.Endpoint) &&
            (!Uri.TryCreate(service.Endpoint, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            AppMessageBox.ShowError("请填写有效的 HTTP/HTTPS 服务地址。", "引擎配置无效", this);
            return;
        }

        if (engine.RequiresApiKey && string.IsNullOrWhiteSpace(service.ApiKey))
        {
            AppMessageBox.ShowError($"{engine.Name} 必须填写 API Key。", "引擎配置无效", this);
            return;
        }

        if (engine.SupportsModel &&
            string.IsNullOrWhiteSpace(service.Model) &&
            string.IsNullOrWhiteSpace(engine.DefaultModel))
        {
            AppMessageBox.ShowError("请填写模型名称。", "引擎配置无效", this);
            return;
        }

        var candidate = new AppSettings
        {
            TriggerMode = triggerMode,
            Hotkey = _hotkey.Clone(),
            IgnoreFullscreen = _fullscreenCheckBox.Checked,
            TargetLanguage = GetChoice(_targetCombo, "auto"),
            Translator = kind,
            Services = _services.ToDictionary(item => item.Key.ToString(), item => item.Value.Clone()),
            AutoStart = _autoStartCheckBox.Checked
        };

        var error = _saveHandler(candidate);
        if (!string.IsNullOrWhiteSpace(error))
        {
            AppMessageBox.ShowError(error, "保存设置失败", this);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static bool IsKeyDown(int virtualKey)
        => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    /// <summary>诊断模式使用：切换到指定引擎并刷新配置区，验证联动不出异常。</summary>
    internal void PreviewEngine(TranslatorKind kind)
    {
        SelectChoice(_engineCombo, kind);
        OnEngineChanged();
    }

    private sealed record ChoiceItem<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }
}