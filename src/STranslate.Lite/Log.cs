using System.Text;

namespace STranslate.Lite;

/// <summary>
/// 极简文件日志。只记录异常和关键状态，不记录用户选中的文本。
/// </summary>
internal static class Log
{
    private const long MaxLogFileBytes = 1024 * 1024;
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureDataDirectory();
                RotateIfNeeded();

                var builder = new StringBuilder();
                builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [")
                    .Append(level)
                    .Append("] ")
                    .Append(message);

                if (exception is not null)
                    builder.AppendLine().Append(exception);

                builder.AppendLine();
                File.AppendAllText(AppPaths.LogFile, builder.ToString(), new UTF8Encoding(false));
            }
        }
        catch
        {
            // 日志失败不能影响主流程。
        }
    }

    private static void RotateIfNeeded()
    {
        var file = new FileInfo(AppPaths.LogFile);
        if (!file.Exists || file.Length < MaxLogFileBytes)
            return;

        var previousLog = Path.Combine(AppPaths.DataDirectory, "app.previous.log");
        File.Move(AppPaths.LogFile, previousLog, overwrite: true);
    }
}