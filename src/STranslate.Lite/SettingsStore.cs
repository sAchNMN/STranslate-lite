using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace STranslate.Lite;

/// <summary>
/// 配置文件读写。写入采用临时文件覆盖，避免进程中断造成半截 JSON。
/// </summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string DataDirectory => AppPaths.DataDirectory;

    public string FilePath => AppPaths.SettingsFile;

    public AppSettings Load()
    {
        try
        {
            AppPaths.EnsureDataDirectory();
            if (!File.Exists(FilePath))
                return new AppSettings();

            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex)
        {
            Log.Error("读取配置失败，已回退到默认配置。", ex);
            BackupBrokenSettings();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        AppPaths.EnsureDataDirectory();

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        var tempFile = FilePath + ".tmp";

        try
        {
            File.WriteAllText(tempFile, json, new UTF8Encoding(false));
            File.Move(tempFile, FilePath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch
            {
                // 忽略临时文件清理失败。
            }

            throw;
        }
    }

    private static void BackupBrokenSettings()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
                return;

            var backupName = $"settings.invalid-{DateTime.Now:yyyyMMdd-HHmmss}.json";
            File.Move(AppPaths.SettingsFile, Path.Combine(AppPaths.DataDirectory, backupName), overwrite: true);
            Log.Info($"损坏的配置已备份为 {backupName}。");
        }
        catch (Exception ex)
        {
            Log.Error("备份损坏配置失败。", ex);
        }
    }
}