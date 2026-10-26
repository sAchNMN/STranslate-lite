using System.Text;

namespace STranslate.Lite;

/// <summary>
/// 前台全屏检测。仅使用只读查询，不安装额外钩子。
/// </summary>
internal static class FullscreenDetector
{
    private static readonly HashSet<string> ExcludedWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Flip3D",
        "TaskSwitcherWnd",
        "ForegroundStaging"
    };

    /// <summary>
    /// 判断当前前台窗口是否覆盖了所在显示器的完整边界。
    /// </summary>
    public static bool IsForegroundWindowFullscreen()
    {
        try
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero ||
                foreground == NativeMethods.GetDesktopWindow() ||
                foreground == NativeMethods.GetShellWindow())
            {
                return false;
            }

            // 最大化的普通窗口带有不可见边框，GetWindowRect 可能大于工作区。
            // 但无边框最大化的游戏/播放器仍然是全屏，因此只在存在标题栏时排除。
            if (NativeMethods.IsZoomed(foreground))
            {
                var style = NativeMethods.GetWindowLongPtr(foreground, NativeMethods.GWL_STYLE).ToInt64();
                var hasCaption = (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;
                if (hasCaption)
                    return false;
            }

            var className = GetWindowClassName(foreground);
            if (ExcludedWindowClasses.Contains(className))
                return false;

            if (!NativeMethods.GetWindowRect(foreground, out var windowRect))
                return false;

            var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return false;

            var monitorInfo = new NativeMethods.MONITORINFO
            {
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
            };

            if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
                return false;

            const int tolerance = 1;
            return Math.Abs(windowRect.Left - monitorInfo.Monitor.Left) <= tolerance &&
                   Math.Abs(windowRect.Top - monitorInfo.Monitor.Top) <= tolerance &&
                   Math.Abs(windowRect.Right - monitorInfo.Monitor.Right) <= tolerance &&
                   Math.Abs(windowRect.Bottom - monitorInfo.Monitor.Bottom) <= tolerance;
        }
        catch
        {
            return false;
        }
    }

    private static string GetWindowClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        var length = NativeMethods.GetClassName(window, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : string.Empty;
    }
}