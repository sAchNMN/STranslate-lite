namespace STranslate.Lite;

/// <summary>
/// 统一管理便携/漫游数据路径，便于测试时通过环境变量隔离配置。
/// </summary>
internal static class AppPaths
{
    private const string DataDirectoryEnvironmentVariable = "STRANSLATE_LITE_HOME";

    public static string DataDirectory
    {
        get
        {
            var customDirectory = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(customDirectory))
                return Path.GetFullPath(customDirectory);

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "STranslate.Lite");
        }
    }

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string LogFile => Path.Combine(DataDirectory, "app.log");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}