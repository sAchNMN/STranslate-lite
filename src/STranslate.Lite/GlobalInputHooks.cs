using System.ComponentModel;
using System.Runtime.InteropServices;

namespace STranslate.Lite;

/// <summary>
/// WH_MOUSE_LL 全局鼠标钩子。只在匹配侧键时拦截，并把翻译请求异步派发到 UI 线程，
/// 确保钩子回调本身在极短时间内返回，避免触发 LowLevelHooksTimeout。
/// </summary>
internal sealed class GlobalInputHooks : IDisposable
{
    private readonly Action<Action> _dispatch;
    private readonly NativeMethods.HookProc _hookProc;
    private readonly Control _hookLifetimeController;
    private IntPtr _hookHandle;
    private TriggerMode _triggerMode = TriggerMode.XButton1;
    private bool _ignoreFullscreen = true;
    private bool _suppressXButton1;
    private bool _suppressXButton2;
    private bool _disposed;

    public GlobalInputHooks(Action<Action> dispatch)
    {
        _dispatch = dispatch;
        _hookProc = HookCallback;

        // 用一个不显示的 Control 作为消息窗口，保证在 Application.Run 之前也能安全 BeginInvoke。
        _hookLifetimeController = new Control();
        _ = _hookLifetimeController.Handle;
    }

    public event Action? TranslateRequested;

    public bool IsRunning => _hookHandle != IntPtr.Zero;

    public void Configure(TriggerMode triggerMode, bool ignoreFullscreen)
    {
        _triggerMode = triggerMode;
        _ignoreFullscreen = ignoreFullscreen;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hookHandle != IntPtr.Zero)
            return;

        var moduleHandle = NativeMethods.GetModuleHandle(null);
        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _hookProc,
            moduleHandle,
            0);

        if (_hookHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "安装全局鼠标钩子失败。");

        Log.Info("全局鼠标钩子已安装。");
    }

    public void Stop()
    {
        if (_hookHandle == IntPtr.Zero)
            return;

        if (!NativeMethods.UnhookWindowsHookEx(_hookHandle))
            Log.Error($"卸载全局鼠标钩子失败，错误码：{Marshal.GetLastWin32Error()}。");

        _hookHandle = IntPtr.Zero;
        _suppressXButton1 = false;
        _suppressXButton2 = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        var message = wParam.ToInt32();
        if (message is not (NativeMethods.WM_XBUTTONDOWN or
            NativeMethods.WM_XBUTTONUP or
            NativeMethods.WM_XBUTTONDBLCLK))
        {
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        var button = (int)((data.MouseData >> 16) & 0xFFFF);

        // 先处理之前已经拦截过的按键抬起/双击，保证“按下被吃掉、抬起却漏出去”的情况不会发生。
        if (message == NativeMethods.WM_XBUTTONUP)
        {
            if (button == 1 && _suppressXButton1)
            {
                _suppressXButton1 = false;
                return new IntPtr(1);
            }

            if (button == 2 && _suppressXButton2)
            {
                _suppressXButton2 = false;
                return new IntPtr(1);
            }
        }
        else if (message == NativeMethods.WM_XBUTTONDBLCLK &&
                 ((button == 1 && _suppressXButton1) || (button == 2 && _suppressXButton2)))
        {
            return new IntPtr(1);
        }

        if (message != NativeMethods.WM_XBUTTONDOWN)
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        var buttonMode = button switch
        {
            1 => TriggerMode.XButton1,
            2 => TriggerMode.XButton2,
            _ => TriggerMode.Disabled
        };

        if (_triggerMode != buttonMode || buttonMode == TriggerMode.Disabled)
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        // 全屏时完全放行，避免在游戏/全屏视频里吞掉鼠标侧键或触发 Ctrl+C。
        if (_ignoreFullscreen && FullscreenDetector.IsForegroundWindowFullscreen())
            return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);

        if (button == 1)
            _suppressXButton1 = true;
        else if (button == 2)
            _suppressXButton2 = true;

        _dispatch(() => TranslateRequested?.Invoke());
        return new IntPtr(1);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _hookLifetimeController.Dispose();
    }
}