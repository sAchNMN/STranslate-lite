using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace STranslate.Lite;

/// <summary>
/// 当前用户的“开机自启”：往“启动”文件夹放一个指向本程序的快捷方式。
/// 与主项目 STranslate 的实现保持一致（见其 Utilities.SetStartup / CreateShortcut），
/// 不写 HKCU 的 Run 注册表项，避免注册表被组策略或安全软件限制时连累其它设置。
/// </summary>
internal static class AutoStartService
{
    private const string ShortcutFileName = "STranslate.Lite.lnk";
    private const string ShortcutDescription = "STranslate Lite";

    public static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutFileName);

    /// <summary>
    /// 启用或关闭开机自启。
    /// 关闭时如果本来就没有快捷方式，直接视为成功，不产生任何写入。
    /// </summary>
    public static bool SetEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            if (enabled)
            {
                var executablePath = ResolveExecutablePath();
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    error = "无法定位当前程序路径，开机自启设置失败。";
                    return false;
                }

                var startupFolder = Path.GetDirectoryName(ShortcutPath);
                if (string.IsNullOrWhiteSpace(startupFolder))
                {
                    error = "无法定位当前用户的“启动”文件夹，开机自启设置失败。";
                    return false;
                }

                Directory.CreateDirectory(startupFolder);
                CreateShortcut(ShortcutPath, executablePath);
            }
            else if (File.Exists(ShortcutPath))
            {
                File.Delete(ShortcutPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(enabled ? "创建开机自启快捷方式失败。" : "删除开机自启快捷方式失败。", ex);
            error = enabled
                ? "创建开机自启快捷方式失败，请检查“启动”文件夹权限。"
                : "删除开机自启快捷方式失败，请检查“启动”文件夹权限。";
            return false;
        }
    }

    /// <summary>
    /// 取用于自启的程序路径。正常启动时是 apphost 的 exe；
    /// 如果是用 <c>dotnet xxx.dll</c> 启动的，ProcessPath 是 dotnet.exe，
    /// 这时回退到程序目录下的同名 apphost，避免把 dotnet.exe 写进启动项。
    /// </summary>
    private static string? ResolveExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            !string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var appHostPath = Path.Combine(AppContext.BaseDirectory, "STranslate.Lite.exe");
        return File.Exists(appHostPath) ? appHostPath : null;
    }

    private static void CreateShortcut(string shortcutPath, string executablePath)
    {
        var link = (IShellLink)new ShellLink();
        try
        {
            link.SetDescription(ShortcutDescription);
            link.SetPath(executablePath);

            var workingDirectory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrWhiteSpace(workingDirectory))
                link.SetWorkingDirectory(workingDirectory);

            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);

            link.Save(shortcutPath, false);
        }
        finally
        {
            if (Marshal.IsComObject(link))
                Marshal.ReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink : IPersistFile
    {
        void GetPath(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cchMaxPath,
            IntPtr pfd,
            int fFlags);

        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cchIconPath,
            out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
