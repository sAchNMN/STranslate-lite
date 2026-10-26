using System.ComponentModel;
using System.Runtime.InteropServices;

namespace STranslate.Lite;

/// <summary>
/// 承载 RegisterHotKey 的消息窗口。使用消息专用窗口，不产生可见界面。
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x534C;
    private bool _disposed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "STranslate.Lite.HotkeyWindow",
            Parent = NativeMethods.HWND_MESSAGE,
            Style = 0,
            ExStyle = 0
        });
    }

    public event EventHandler? HotkeyPressed;

    public bool Register(HotkeyDefinition hotkey, out string? error)
    {
        error = null;
        if (hotkey is null || !hotkey.IsValid)
        {
            error = "快捷键无效，请至少包含一个修饰键。";
            return false;
        }

        Unregister();

        var modifiers = NativeMethods.MOD_NOREPEAT;
        if (hotkey.Ctrl)
            modifiers |= NativeMethods.MOD_CONTROL;
        if (hotkey.Alt)
            modifiers |= NativeMethods.MOD_ALT;
        if (hotkey.Shift)
            modifiers |= NativeMethods.MOD_SHIFT;
        if (hotkey.Win)
            modifiers |= NativeMethods.MOD_WIN;

        if (!NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers, (uint)hotkey.VirtualKey))
        {
            var code = Marshal.GetLastWin32Error();
            error = code == 1409
                ? $"快捷键 {hotkey.Display} 已被其它程序占用。"
                : $"注册快捷键 {hotkey.Display} 失败，Win32 错误码：{code}。";
            Log.Error(error);
            return false;
        }

        Log.Info($"已注册全局快捷键 {hotkey.Display}。");
        return true;
    }

    public void Unregister() => NativeMethods.UnregisterHotKey(Handle, HotkeyId);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Unregister();
        DestroyHandle();
    }
}