using Microsoft.Win32;

namespace STranslate.Lite;

/// <summary>
/// 当前用户的“开机自启”注册表项管理，不需要管理员权限。
/// </summary>
internal static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "STranslate.Lite";

    public static bool SetEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                var executablePath = Application.ExecutablePath;
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    error = "无法定位当前程序路径，开机自启设置失败。";
                    return false;
                }

                key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("写入开机自启注册表失败。", ex);
            error = "写入开机自启注册表失败，请检查当前用户的注册表权限。";
            return false;
        }
    }
}