using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using STranslate.Lite.Engines;

namespace STranslate.Lite;

/// <summary>
/// 托盘应用入口：管理生命周期、钩子、热键、设置窗口和托盘菜单。
/// 主界面常驻不可见，仅保留一个 NotifyIcon。
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore = new();
    private readonly PopupForm _popup;
    private readonly Control _dispatcher;
    private readonly GlobalInputHooks _hooks;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly TriggerCoordinator _coordinator;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _appIcon;
    private readonly ToolStripMenuItem _triggerMenu;
    private readonly ToolStripMenuItem _engineMenu;
    private readonly ToolStripMenuItem _fullscreenItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly EventWaitHandle _activationEvent;
    private readonly CancellationTokenSource _activationStop = new();
    private readonly Thread _activationThread;
    private readonly Dictionary<TriggerMode, ToolStripMenuItem> _triggerItems = [];
    private readonly Dictionary<TranslatorKind, ToolStripMenuItem> _engineItems = [];

    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private bool _disposed;

    public TrayApplicationContext(EventWaitHandle activationEvent)
    {
        _settings = _settingsStore.Load();
        _activationEvent = activationEvent;

        _dispatcher = new Control();
        _ = _dispatcher.Handle;

        _popup = new PopupForm();
        _coordinator = new TriggerCoordinator(() => _settings, _popup);
        _hooks = new GlobalInputHooks(DispatchToUi);
        _hooks.TranslateRequested += OnTranslateRequested;
        _hooks.Configure(_settings.TriggerMode, _settings.IgnoreFullscreen);

        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;

        _appIcon = LoadApplicationIcon();
        var menu = BuildContextMenu(
            out var triggerMenu,
            out var engineMenu,
            out var fullscreenItem,
            out var autoStartItem);
        _triggerMenu = triggerMenu;
        _engineMenu = engineMenu;
        _fullscreenItem = fullscreenItem;
        _autoStartItem = autoStartItem;

        _notifyIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "STranslate Lite · 划词翻译",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => OpenSettings();

        try
        {
            _hooks.Start();
        }
        catch
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _appIcon.Dispose();
            _hotkeyWindow.Dispose();
            _hooks.Dispose();
            _dispatcher.Dispose();
            _coordinator.Dispose();
            _popup.Dispose();
            throw;
        }

        if (!File.Exists(_settingsStore.FilePath))
        {
            try
            {
                _settingsStore.Save(_settings);
            }
            catch (Exception ex)
            {
                Log.Error("写入默认配置失败。", ex);
            }
        }

        var startupError = ApplyRuntime(_settings);
        if (!string.IsNullOrWhiteSpace(startupError))
        {
            Log.Error($"启动时应用设置失败：{startupError}");
            _dispatcher.BeginInvoke(new Action(() => NotifyError("部分设置未生效", startupError)));
        }

        if (!AutoStartService.SetEnabled(_settings.AutoStart, out var autoStartError) &&
            !string.IsNullOrWhiteSpace(autoStartError))
        {
            Log.Error($"同步开机自启失败：{autoStartError}");
            _dispatcher.BeginInvoke(new Action(() => NotifyError("开机自启设置失败", autoStartError)));
        }

        UpdateMenuState();
        Log.Info("STranslate Lite 已启动。");

        _activationThread = new Thread(ActivationLoop)
        {
            IsBackground = true,
            Name = "STranslate.Lite.Activation"
        };
        _activationThread.Start();
    }

    private void ActivationLoop()
    {
        var waitHandles = new WaitHandle[] { _activationEvent, _activationStop.Token.WaitHandle };
        Log.Info("再次启动监听线程已启动。");
        while (!_activationStop.IsCancellationRequested)
        {
            var index = WaitHandle.WaitAny(waitHandles);
            Log.Info($"再次启动监听收到信号，index={index}。");
            if (index != 0)
                return;

            try
            {
                if (!_disposed)
                    _dispatcher.BeginInvoke(new Action(() =>
                    {
                        Log.Info("再次启动请求已进入 UI 线程，准备打开设置。");
                        OpenSettings();
                    }));
            }
            catch (Exception ex)
            {
                Log.Error("处理再次启动请求失败。", ex);
            }
        }
    }

    private ContextMenuStrip BuildContextMenu(
        out ToolStripMenuItem triggerMenu,
        out ToolStripMenuItem engineMenu,
        out ToolStripMenuItem fullscreenItem,
        out ToolStripMenuItem autoStartItem)
    {
        var menu = new ContextMenuStrip();

        var translateItem = new ToolStripMenuItem("立即翻译当前选中文本");
        translateItem.Click += (_, _) => OnTranslateRequested();
        menu.Items.Add(translateItem);
        menu.Items.Add(new ToolStripSeparator());

        triggerMenu = new ToolStripMenuItem("触发方式");
        AddTriggerItem(triggerMenu, "XButton1（鼠标后退键）", TriggerMode.XButton1);
        AddTriggerItem(triggerMenu, "XButton2（鼠标前进键）", TriggerMode.XButton2);
        AddTriggerItem(triggerMenu, "键盘组合键", TriggerMode.Hotkey);
        AddTriggerItem(triggerMenu, "禁用", TriggerMode.Disabled);
        menu.Items.Add(triggerMenu);

        engineMenu = new ToolStripMenuItem("翻译引擎");
        foreach (var engine in EngineRegistry.All)
            AddEngineItem(engineMenu, engine);
        menu.Items.Add(engineMenu);

        var settingsItem = new ToolStripMenuItem("快捷键与翻译设置…");
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());

        fullscreenItem = new ToolStripMenuItem("全屏时禁用翻译");
        fullscreenItem.Click += (_, _) => ToggleFullscreen();
        menu.Items.Add(fullscreenItem);

        autoStartItem = new ToolStripMenuItem("开机自动启动");
        autoStartItem.Click += (_, _) => ToggleAutoStart();
        menu.Items.Add(autoStartItem);

        var openFolderItem = new ToolStripMenuItem("打开配置目录");
        openFolderItem.Click += (_, _) => OpenConfigFolder();
        menu.Items.Add(openFolderItem);

        menu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) => UpdateMenuState();
        return menu;
    }

    private void AddTriggerItem(ToolStripMenuItem parent, string text, TriggerMode mode)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => ChangeTriggerMode(mode);
        parent.DropDownItems.Add(item);
        _triggerItems[mode] = item;
    }

    private void AddEngineItem(ToolStripMenuItem parent, TranslateEngine engine)
    {
        var item = new ToolStripMenuItem($"{engine.Name} · {engine.Group}");
        item.Click += (_, _) => ChangeEngine(engine.Kind);
        parent.DropDownItems.Add(item);
        _engineItems[engine.Kind] = item;
    }

    private void ChangeEngine(TranslatorKind kind)
    {
        if (_disposed || _settings.Translator == kind)
            return;

        var candidate = _settings.Clone();
        candidate.Translator = kind;
        var error = SaveAndApply(candidate);
        if (!string.IsNullOrWhiteSpace(error))
            NotifyError("切换翻译引擎失败", error);
    }

    private void OnTranslateRequested() => _coordinator.RequestTrigger();

    private void OnHotkeyPressed(object? sender, EventArgs e) => _coordinator.RequestTrigger();

    private void DispatchToUi(Action action)
    {
        try
        {
            if (!_dispatcher.IsDisposed)
                _dispatcher.BeginInvoke(action);
        }
        catch (Exception ex)
        {
            Log.Error("派发钩子事件失败。", ex);
        }
    }

    private void ChangeTriggerMode(TriggerMode mode)
    {
        if (_disposed || _settings.TriggerMode == mode)
            return;

        var candidate = _settings.Clone();
        candidate.TriggerMode = mode;
        var error = SaveAndApply(candidate);
        if (!string.IsNullOrWhiteSpace(error))
            NotifyError("切换触发方式失败", error);
    }

    private void ToggleFullscreen()
    {
        if (_disposed)
            return;

        var candidate = _settings.Clone();
        candidate.IgnoreFullscreen = !candidate.IgnoreFullscreen;
        var error = SaveAndApply(candidate);
        if (!string.IsNullOrWhiteSpace(error))
            NotifyError("修改全屏设置失败", error);
    }

    private void ToggleAutoStart()
    {
        if (_disposed)
            return;

        var candidate = _settings.Clone();
        candidate.AutoStart = !candidate.AutoStart;
        var error = SaveAndApply(candidate);
        if (!string.IsNullOrWhiteSpace(error))
            NotifyError("修改开机自启失败", error);
    }

    /// <summary>
    /// 先应用运行时设置，再持久化；热键或配置写入失败时回滚到原设置。
    /// 开机自启是独立的副作用，失败只提示并回退该选项，不影响热键与其它配置。
    /// </summary>
    private string? SaveAndApply(AppSettings candidate)
    {
        candidate.Normalize();

        var previous = _settings;
        var applyError = ApplyRuntime(candidate);
        if (!string.IsNullOrWhiteSpace(applyError))
        {
            _ = ApplyRuntime(previous);
            return applyError;
        }

        // 参考主项目 STranslate：自启失败不阻断设置保存，只把该选项恢复到实际状态。
        if (!AutoStartService.SetEnabled(candidate.AutoStart, out var autoStartError))
        {
            Log.Error($"同步开机自启失败：{autoStartError}");
            candidate.AutoStart = previous.AutoStart;
            NotifyError("开机自启设置失败", autoStartError ?? "无法写入“启动”文件夹。");
        }

        try
        {
            _settingsStore.Save(candidate);
        }
        catch (Exception ex)
        {
            Log.Error("保存配置失败。", ex);
            _ = ApplyRuntime(previous);

            if (IntegrityLevel.IsLow())
            {
                return "写入配置文件失败：当前程序以受限权限运行，无法写入用户配置目录。" +
                       "请在“资源管理器”中直接双击 STranslate.Lite.exe 启动。";
            }

            return "写入配置文件失败，请检查配置目录权限。";
        }

        _settings = candidate;
        UpdateMenuState();
        return null;
    }

    private string? ApplyRuntime(AppSettings settings)
    {
        _hotkeyWindow.Unregister();

        if (settings.TriggerMode == TriggerMode.Hotkey)
        {
            if (!_hotkeyWindow.Register(settings.Hotkey, out var hotkeyError))
                return hotkeyError ?? "注册快捷键失败。";
        }

        _hooks.Configure(settings.TriggerMode, settings.IgnoreFullscreen);
        return null;
    }

    private void OpenSettings()
    {
        if (_disposed)
            return;

        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            NativeMethods.ForceForeground(_settingsForm.Handle);
            return;
        }

        var form = new SettingsForm(_settings, SaveAndApply);
        _settingsForm = form;
        form.Shown += (_, _) =>
        {
            form.Activate();
            NativeMethods.ForceForeground(form.Handle);
        };
        try
        {
            form.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error("打开设置窗口失败。", ex);
            NotifyError("打开设置失败", ex.Message);
        }
        finally
        {
            _settingsForm = null;
            form.Dispose();
        }
    }

    private void OpenConfigFolder()
    {
        try
        {
            AppPaths.EnsureDataDirectory();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_settingsStore.DataDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error("打开配置目录失败。", ex);
            NotifyError("打开配置目录失败", ex.Message);
        }
    }

    private void UpdateMenuState()
    {
        if (_disposed)
            return;

        _triggerMenu.Text = $"触发方式：{GetTriggerDisplayName(_settings.TriggerMode)}";
        foreach (var (mode, item) in _triggerItems)
            item.Checked = _settings.TriggerMode == mode;

        _engineMenu.Text = $"翻译引擎：{EngineRegistry.Get(_settings.Translator).Name}";
        foreach (var (kind, item) in _engineItems)
            item.Checked = _settings.Translator == kind;

        _fullscreenItem.Checked = _settings.IgnoreFullscreen;
        _autoStartItem.Checked = _settings.AutoStart;
    }

    private static string GetTriggerDisplayName(TriggerMode mode) => mode switch
    {
        TriggerMode.XButton1 => "XButton1",
        TriggerMode.XButton2 => "XButton2",
        TriggerMode.Hotkey => "快捷键",
        _ => "禁用"
    };

    private void NotifyError(string title, string text)
    {
        if (_disposed)
            return;

        try
        {
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.ShowBalloonTip(4500);
        }
        catch (Exception ex)
        {
            Log.Error("显示托盘通知失败。", ex);
        }
    }

    private void ExitApplication()
    {
        if (_disposed)
            return;

        _notifyIcon.Visible = false;
        ExitThread();
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(path))
                return new Icon(path);
        }
        catch (Exception ex)
        {
            Log.Error("加载应用图标失败。", ex);
        }

        try
        {
            var associated = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (associated is not null)
                return associated;
        }
        catch (Exception ex)
        {
            Log.Error("从可执行文件提取图标失败。", ex);
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private void DisposeResources()
    {
        if (_disposed)
            return;

        _disposed = true;
        _activationStop.Cancel();
        if (_activationThread.IsAlive && _activationThread != Thread.CurrentThread)
            _activationThread.Join(200);
        _activationStop.Dispose();

        _settingsForm?.Dispose();
        _coordinator.Dispose();
        _hotkeyWindow.Dispose();
        _hooks.Dispose();
        _popup.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _appIcon.Dispose();
        _dispatcher.Dispose();
        Log.Info("STranslate Lite 已退出。");
    }

    protected override void ExitThreadCore()
    {
        DisposeResources();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DisposeResources();

        base.Dispose(disposing);
    }
}