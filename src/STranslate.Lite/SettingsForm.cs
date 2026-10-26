using System.Windows.Forms;

namespace STranslate.Lite;

/// <summary>
/// 常规设置窗口。只有这个窗口会主动获取焦点，翻译浮窗始终不会。
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly Func<AppSettings, string?> _saveHandler;
    private readonly ComboBox _triggerCombo;
    private readonly ComboBox _targetCombo;
    private readonly ComboBox _engineCombo;
    private readonly TextBox _hotkeyBox;
    private readonly TextBox _endpointBox;
    private readonly TextBox _apiKeyBox;
    private readonly TextBox _modelBox;
    private readonly CheckBox _fullscreenCheckBox;
    private readonly CheckBox _autoStartCheckBox;
    private readonly GroupBox _openAiGroup;
    private readonly Label _hotkeyHint;

    private HotkeyDefinition _hotkey;
    private bool _capturingHotkey;

    public SettingsForm(AppSettings settings, Func<AppSettings, string?> saveHandler)
    {
        _saveHandler = saveHandler;
        _hotkey = settings.Hotkey.Clone();

        Text = "STranslate Lite 设置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(570, 700);
        MinimumSize = new Size(570, 620);
        Padding = new Padding(10);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(2)
        };
        for (var index = 0; index < 5; index++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

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

        _hotkeyHint = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "点击输入框后按下组合键；必须包含 Ctrl / Alt / Shift / Win 中至少一个。"
        };

        _engineCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _engineCombo.Items.AddRange(
        [
            new ChoiceItem<TranslatorKind>("Google 免费接口（无需 API Key）", TranslatorKind.Google),
            new ChoiceItem<TranslatorKind>("OpenAI 兼容接口（需 API Key）", TranslatorKind.OpenAiCompatible)
        ]);
        SelectChoice(_engineCombo, settings.Translator);
        _engineCombo.SelectedIndexChanged += (_, _) => UpdateEngineState();

        _targetCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _targetCombo.Items.AddRange(
        [
            new ChoiceItem<string>("自动（中文→英文，其它→中文）", "auto"),
            new ChoiceItem<string>("简体中文", "zh-CN"),
            new ChoiceItem<string>("English", "en"),
            new ChoiceItem<string>("日本語", "ja"),
            new ChoiceItem<string>("한국어", "ko"),
            new ChoiceItem<string>("繁體中文", "zh-TW"),
            new ChoiceItem<string>("Français", "fr"),
            new ChoiceItem<string>("Deutsch", "de")
        ]);
        SelectChoice(_targetCombo, settings.TargetLanguage);

        _endpointBox = CreateTextBox(settings.OpenAi.Endpoint);
        _apiKeyBox = CreateTextBox(settings.OpenAi.ApiKey);
        _apiKeyBox.UseSystemPasswordChar = true;
        _modelBox = CreateTextBox(settings.OpenAi.Model);

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

        root.Controls.Add(CreateTriggerGroup(), 0, 0);
        root.Controls.Add(CreateEngineGroup(), 0, 1);
        _openAiGroup = CreateOpenAiGroup();
        root.Controls.Add(_openAiGroup, 0, 2);
        root.Controls.Add(CreateBehaviorGroup(), 0, 3);
        root.Controls.Add(CreateButtonPanel(), 0, 4);

        Controls.Add(root);
        UpdateEngineState();
    }

    private GroupBox CreateTriggerGroup()
    {
        var group = CreateGroup("触发方式");
        var table = CreateFieldTable();

        AddField(table, 0, "触发方式", _triggerCombo);
        AddField(table, 1, "键盘快捷键", _hotkeyBox);

        table.Controls.Add(_hotkeyHint, 1, 2);
        table.SetColumnSpan(_hotkeyHint, 2);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            ForeColor = SystemColors.GrayText,
            Text = "侧键触发时会拦截该按键原本的“前进/后退”行为；全屏应用下自动放行。"
        };
        table.Controls.Add(note, 0, 3);
        table.SetColumnSpan(note, 2);

        group.Controls.Add(table);
        return group;
    }

    private GroupBox CreateEngineGroup()
    {
        var group = CreateGroup("翻译");
        var table = CreateFieldTable();

        AddField(table, 0, "翻译引擎", _engineCombo);
        AddField(table, 1, "目标语言", _targetCombo);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            ForeColor = SystemColors.GrayText,
            Text = "Google 免费接口无需 Key；如网络不可用，可在下方切换到 OpenAI 兼容的大模型接口。"
        };
        table.Controls.Add(note, 0, 2);
        table.SetColumnSpan(note, 2);

        group.Controls.Add(table);
        return group;
    }

    private GroupBox CreateOpenAiGroup()
    {
        var group = CreateGroup("OpenAI 兼容接口（可选）");
        var table = CreateFieldTable();

        AddField(table, 0, "API 地址", _endpointBox);
        AddField(table, 1, "API Key", _apiKeyBox);
        AddField(table, 2, "模型", _modelBox);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            ForeColor = SystemColors.GrayText,
            Text = "例如 DeepSeek：https://api.deepseek.com/v1/chat/completions，模型 deepseek-chat。Key 会以明文保存在本机配置文件中。"
        };
        table.Controls.Add(note, 0, 3);
        table.SetColumnSpan(note, 2);

        group.Controls.Add(table);
        return group;
    }

    private GroupBox CreateBehaviorGroup()
    {
        var group = CreateGroup("行为");
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(6)
        };
        panel.Controls.Add(_fullscreenCheckBox);
        panel.Controls.Add(_autoStartCheckBox);
        group.Controls.Add(panel);
        return group;
    }

    private Control CreateButtonPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };

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

        panel.Controls.Add(saveButton);
        panel.Controls.Add(cancelButton);
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        var pathLabel = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = $"配置文件：{AppPaths.SettingsFile}",
            Margin = new Padding(6, 12, 8, 0)
        };
        panel.Controls.Add(pathLabel);
        return panel;
    }

    private static GroupBox CreateGroup(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(8),
        Margin = new Padding(0, 0, 0, 10)
    };

    private static TableLayoutPanel CreateFieldTable()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(4)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static void AddField(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowCount = Math.Max(table.RowCount, row + 1);
        var text = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 7, 3, 3)
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 4, 3, 4);
        table.Controls.Add(text, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static TextBox CreateTextBox(string text) => new()
    {
        Text = text,
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

    private void UpdateEngineState()
    {
        var isOpenAi = GetChoice(_engineCombo, TranslatorKind.Google) == TranslatorKind.OpenAiCompatible;
        _openAiGroup.Enabled = isOpenAi;
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

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        var triggerMode = GetChoice(_triggerCombo, TriggerMode.XButton1);
        if (triggerMode == TriggerMode.Hotkey && !_hotkey.IsValid)
        {
            AppMessageBox.ShowError("请先录制一个包含修饰键的快捷键。", "快捷键无效", this);
            return;
        }

        var engine = GetChoice(_engineCombo, TranslatorKind.Google);
        var endpoint = _endpointBox.Text.Trim();
        var apiKey = _apiKeyBox.Text.Trim();
        var model = _modelBox.Text.Trim();

        if (engine == TranslatorKind.OpenAiCompatible)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                AppMessageBox.ShowError("请填写有效的 HTTP/HTTPS API 地址。", "接口配置无效", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AppMessageBox.ShowError("使用 OpenAI 兼容接口时必须填写 API Key。", "接口配置无效", this);
                return;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                AppMessageBox.ShowError("请填写模型名称。", "接口配置无效", this);
                return;
            }
        }

        var candidate = new AppSettings
        {
            TriggerMode = triggerMode,
            Hotkey = _hotkey.Clone(),
            IgnoreFullscreen = _fullscreenCheckBox.Checked,
            TargetLanguage = GetChoice(_targetCombo, "auto"),
            Translator = engine,
            OpenAi = new OpenAiCompatibleOptions
            {
                Endpoint = endpoint,
                ApiKey = apiKey,
                Model = model
            },
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

    private sealed record ChoiceItem<T>(string Label, T Value)
    {
        public override string ToString() => Label;
    }
}