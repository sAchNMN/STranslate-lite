using System.Runtime.InteropServices;
using System.Text;

namespace STranslate.Lite;

/// <summary>
/// 本程序使用的 Win32 互操作声明。全部按 x64 进程编译，避免指针宽度差异。
/// </summary>
internal static class NativeMethods
{
    internal const int WH_MOUSE_LL = 14;
    internal const int WH_KEYBOARD_LL = 13;

    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_XBUTTONDOWN = 0x020B;
    internal const int WM_XBUTTONUP = 0x020C;
    internal const int WM_XBUTTONDBLCLK = 0x020D;
    internal const int WM_MOUSEACTIVATE = 0x0021;
    internal const int WM_HOTKEY = 0x0312;
    internal const int WM_QUIT = 0x0012;

    internal const int MA_NOACTIVATE = 3;
    internal const int VK_ESCAPE = 0x1B;
    internal const int VK_LSHIFT = 0xA0;
    internal const int VK_RSHIFT = 0xA1;
    internal const int VK_LCONTROL = 0xA2;
    internal const int VK_RCONTROL = 0xA3;
    internal const int VK_LMENU = 0xA4;
    internal const int VK_RMENU = 0xA5;
    internal const int VK_LWIN = 0x5B;
    internal const int VK_RWIN = 0x5C;
    internal const int VK_SHIFT = 0x10;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_MENU = 0x12;
    internal const int VK_C = 0x43;

    /// <summary>发送 Ctrl+C 前需要检查并抬起的修饰键。</summary>
    private static readonly int[] ModifierKeys =
    [
        VK_LSHIFT, VK_RSHIFT, VK_LCONTROL, VK_RCONTROL, VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN
    ];

    /// <summary>等待用户松开热键的最长时间。超过后强制抬起，避免取词一直卡住。</summary>
    internal const int TriggerKeyReleaseTimeoutMs = 450;

    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    internal enum MONITOR_DPI_TYPE
    {
        MDT_EFFECTIVE_DPI = 0,
        MDT_ANGULAR_DPI = 1,
        MDT_RAW_DPI = 2
    }

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;

    internal const int GWL_EXSTYLE = -20;
    internal const int GWL_STYLE = -16;
    internal const long WS_CAPTION = 0x00C00000;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_TOPMOST = 0x00000008;
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const int CS_DROPSHADOW = 0x00020000;

    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWCP_ROUND = 2;

    internal static readonly IntPtr HWND_MESSAGE = new(-3);
    internal static readonly IntPtr HWND_TOPMOST = new(-1);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDesktopWindow();

    /// <summary>
    /// 将指定窗口带到前台。Windows 会限制后台进程抢焦点，这里临时附加到前台线程以完成用户显式请求。
    /// </summary>
    internal static void ForceForeground(IntPtr window)
    {
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        var attached = AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = BringWindowToTop(window);
            _ = SetForegroundWindow(window);
            _ = SetFocus(window);
        }
        finally
        {
            if (attached)
                _ = AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(
        IntPtr hmonitor,
        MONITOR_DPI_TYPE dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKey(uint uCode, uint uMapType);

    /// <summary>
    /// 发送 Ctrl+C。
    /// </summary>
    /// <param name="triggerVirtualKey">
    /// 触发热键的虚拟键码；鼠标触发时传 0。发送前会等待该键和所有修饰键真正松开，
    /// 避免 Alt+X 之类的组合键让目标程序收到 Ctrl+Alt+C，或让浏览器把 Alt 当作菜单键。
    /// </param>
    /// <remarks>
    /// 之前的做法是在注入 Ctrl+C 前直接发送修饰键抬起消息。对浏览器来说，一个孤立的
    /// Alt 抬起消息就可能激活菜单并把键盘焦点从网页移走，随后注入的 Ctrl+C 根本不会
    /// 被页面处理，表现就是“明明选中了文字却复制不到”。现在改为先等用户松键；
    /// 只有用户长按不松时才强制抬起。
    /// </remarks>
    internal static bool SendCtrlC(int triggerVirtualKey = 0)
    {
        if (!WaitForKeysReleased(triggerVirtualKey, TriggerKeyReleaseTimeoutMs))
        {
            Log.Info("触发热键仍被按住，强制抬起后再发送 Ctrl+C。");
            if (!ReleasePressedKeys(triggerVirtualKey))
            {
                Log.Error("强制抬起按键失败，无法安全发送 Ctrl+C。");
                return false;
            }
        }

        // 给目标程序一点时间处理最后一次按键抬起，再注入复制组合键。
        Thread.Sleep(20);

        var down = new[]
        {
            CreateKeyboardInput(VK_CONTROL, keyUp: false),
            CreateKeyboardInput(VK_C, keyUp: false)
        };
        var up = new[]
        {
            CreateKeyboardInput(VK_C, keyUp: true),
            CreateKeyboardInput(VK_CONTROL, keyUp: true)
        };

        var downSent = SendInput((uint)down.Length, down, Marshal.SizeOf<INPUT>());
        Thread.Sleep(15);
        var upSent = SendInput((uint)up.Length, up, Marshal.SizeOf<INPUT>());

        if (downSent == down.Length && upSent == up.Length)
            return true;

        Log.Error($"发送 Ctrl+C 失败，Win32 错误码：{Marshal.GetLastWin32Error()}。");
        return false;
    }

    /// <summary>
    /// 发送一次 Esc。用于关闭 Alt 组合键可能打开的菜单栏，让键盘焦点回到原窗口。
    /// </summary>
    internal static bool SendEscape()
    {
        var down = CreateKeyboardInput(VK_ESCAPE, keyUp: false);
        var up = CreateKeyboardInput(VK_ESCAPE, keyUp: true);
        if (SendInput(1, new[] { down }, Marshal.SizeOf<INPUT>()) != 1)
            return false;

        Thread.Sleep(15);
        return SendInput(1, new[] { up }, Marshal.SizeOf<INPUT>()) == 1;
    }

    /// <summary>
    /// 等待触发键与修饰键从物理状态上松开。热键触发时 Alt/Ctrl 仍然按着，
    /// 立即注入 C 会变成组合键，目标程序不会执行普通复制。
    /// </summary>
    private static bool WaitForKeysReleased(int triggerVirtualKey, int timeoutMs)
    {
        var keys = BuildTriggerKeys(triggerVirtualKey);
        var deadline = Environment.TickCount64 + timeoutMs;

        while (Environment.TickCount64 < deadline)
        {
            if (!keys.Any(IsKeyDown))
                return true;

            Thread.Sleep(10);
        }

        return !keys.Any(IsKeyDown);
    }

    /// <summary>
    /// 强制抬起触发键和所有修饰键。仅在用户长按热键、等待超时时使用。
    /// </summary>
    private static bool ReleasePressedKeys(int triggerVirtualKey)
    {
        var keys = BuildTriggerKeys(triggerVirtualKey);
        if (keys.Length == 0)
            return true;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var pressed = keys.Where(IsKeyDown).ToArray();
            if (pressed.Length == 0)
                return true;

            var release = pressed.Select(key => CreateKeyboardInput(key, keyUp: true)).ToArray();
            if (SendInput((uint)release.Length, release, Marshal.SizeOf<INPUT>()) != release.Length)
            {
                Log.Error($"抬起按键失败，Win32 错误码：{Marshal.GetLastWin32Error()}。");
                return false;
            }

            Thread.Sleep(15);
        }

        return !keys.Any(IsKeyDown);
    }

    private static int[] BuildTriggerKeys(int triggerVirtualKey)
    {
        if (triggerVirtualKey <= 0)
            return ModifierKeys;

        var keys = new int[ModifierKeys.Length + 1];
        ModifierKeys.CopyTo(keys, 0);
        keys[^1] = triggerVirtualKey;
        return keys;
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static INPUT CreateKeyboardInput(int virtualKey, bool keyUp) => new()
    {
        Type = INPUT_KEYBOARD,
        Union = new InputUnion
        {
            Keyboard = new KEYBDINPUT
            {
                VirtualKey = (ushort)virtualKey,
                // 同时填上扫描码。部分程序只认扫描码；不设置 KEYEVENTF_SCANCODE 时
                // 系统仍以 VirtualKey 为准，因此不会影响普通程序的兼容性。
                ScanCode = (ushort)MapVirtualKey((uint)virtualKey, 0),
                Flags = keyUp ? KEYEVENTF_KEYUP : 0
            }
        }
    };

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public uint Size;
        public RECT Monitor;
        public RECT WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;

        [FieldOffset(0)]
        public HARDWAREINPUT Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    /// <summary>命令行诊断模式把输出接回调用方控制台。</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachConsole(int processId);
}