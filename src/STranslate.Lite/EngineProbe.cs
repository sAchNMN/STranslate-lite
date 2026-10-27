using System.Diagnostics;
using System.Text;
using STranslate.Lite.Engines;

namespace STranslate.Lite;

/// <summary>
/// 命令行引擎诊断：逐家真实请求一次，输出耗时、译文或失败原因。
/// 用法：STranslate.Lite.exe --probe [--out 文件] [--engine Google,DeepL] [--timeout 秒]
/// </summary>
internal static class EngineProbe
{
    private const int DefaultTimeoutSeconds = 20;

    public static int Run(string[] args)
    {
        var outPath = Path.Combine(Path.GetTempPath(), "stranslate-lite-probe.txt");
        var timeoutSeconds = DefaultTimeoutSeconds;
        var filter = new List<TranslatorKind>();
        var testText = "Hello, world! This is a translation test.";
        string? requestedTarget = null;
        var fullText = false;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--out" when index + 1 < args.Length:
                    outPath = Path.GetFullPath(args[++index]);
                    break;

                case "--timeout" when index + 1 < args.Length &&
                                      int.TryParse(args[++index], out var seconds):
                    timeoutSeconds = Math.Clamp(seconds, 3, 120);
                    break;

                case "--text" when index + 1 < args.Length:
                    testText = args[++index];
                    break;

                case "--target" when index + 1 < args.Length:
                    requestedTarget = args[++index];
                    break;

                case "--full":
                    fullText = true;
                    break;

                case "--engine" when index + 1 < args.Length:
                    foreach (var name in args[++index].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (Enum.TryParse<TranslatorKind>(name.Trim(), ignoreCase: true, out var kind))
                            filter.Add(kind);
                    }

                    break;
            }
        }

        var engines = filter.Count == 0
            ? EngineRegistry.All
            : EngineRegistry.All.Where(engine => filter.Contains(engine.Kind)).ToArray();

        TryAttachConsole();

        var settings = new SettingsStore().Load();
        var lines = new List<string>
        {
            $"# STranslate Lite 引擎诊断 {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"# 测试句：{testText}",
            $"# 目标语言：{requestedTarget ?? "zh-CN（Apertium 使用 es）"}",
            $"# 超时：{timeoutSeconds} 秒",
            string.Empty
        };

        var success = 0;
        var failure = 0;

        foreach (var engine in engines)
        {
            var service = settings.GetService(engine.Kind);
            var target = requestedTarget ??
                         (engine.Kind == TranslatorKind.Apertium ? "es" : "zh-CN");
            var watch = Stopwatch.StartNew();
            string line;

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var result = TranslationService
                    .TestAsync(engine.Kind, service, target, testText, timeout.Token)
                    .GetAwaiter()
                    .GetResult();

                watch.Stop();
                success++;
                line = $"{engine.Kind,-14} OK   {watch.ElapsedMilliseconds,6} ms  {OneLine(result.Text, fullText)}";
            }
            catch (Exception ex)
            {
                watch.Stop();
                failure++;
                var message = Describe(ex);
                line = $"{engine.Kind,-14} FAIL {watch.ElapsedMilliseconds,6} ms  {OneLine(message, fullText)}";
            }

            lines.Add(line);
            Console.WriteLine(line);
            TryWriteReport(outPath, lines);
        }

        lines.Add(string.Empty);
        lines.Add($"# 合计：成功 {success}，失败 {failure}");
        TryWriteReport(outPath, lines);

        Console.WriteLine();
        Console.WriteLine($"报告已写入：{outPath}");
        return failure == 0 ? 0 : 1;
    }

    private static void TryWriteReport(string path, IReadOnlyCollection<string> lines)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"写入报告失败：{ex.Message}");
        }
    }

    private static string Describe(Exception exception)
    {
        var message = exception.Message;
        var root = exception.GetBaseException();
        if (!ReferenceEquals(root, exception) &&
            !string.IsNullOrWhiteSpace(root.Message) &&
            !message.Contains(root.Message, StringComparison.Ordinal))
        {
            return $"{message}（{root.Message}）";
        }

        return message;
    }

    private static string OneLine(string text, bool keepFull)
    {
        var value = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (keepFull)
            return value;

        return value.Length <= 160 ? value : value[..160] + "…";
    }

    private static void TryAttachConsole()
    {
        try
        {
            if (!NativeMethods.AttachConsole(-1))
                return;

            var stdout = Console.OpenStandardOutput();
            Console.SetOut(new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true });
        }
        catch
        {
            // 没有可用控制台时只依赖报告文件。
        }
    }
}