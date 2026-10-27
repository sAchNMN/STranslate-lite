using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using STranslate.Lite.Engines;

namespace STranslate.Lite;

/// <summary>
/// 无界面自检：引擎基础逻辑（分片 / 语言码）+ 配置读写与 v1 迁移 + 设置窗口构造与全部引擎切换。
/// 用法：STranslate.Lite.exe --uicheck
/// </summary>
internal static class UiSmokeTest
{
    public static int Run()
    {
        var reportPath = Path.Combine(Path.GetTempPath(), "stranslate-lite-uicheck.txt");
        var originalHome = Environment.GetEnvironmentVariable("STRANSLATE_LITE_HOME");
        var tempHome = Path.Combine(
            Path.GetTempPath(),
            "stranslate-lite-selfcheck-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("STRANSLATE_LITE_HOME", tempHome);

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var primitives = VerifyEnginePrimitives();
            var migration = VerifySettingsRoundTrip();
            var settings = new SettingsStore().Load();

            using var form = new SettingsForm(settings, _ => null);
            form.CreateControl();

            foreach (var engine in EngineRegistry.All)
                form.PreviewEngine(engine.Kind);

            var imagePath = Path.Combine(Path.GetTempPath(), "stranslate-lite-settings.png");
            form.PreviewEngine(TranslatorKind.Google);
            CaptureWindow(form, imagePath);

            var configImagePath = Path.Combine(Path.GetTempPath(), "stranslate-lite-settings-config.png");
            form.PreviewEngine(TranslatorKind.OpenAiCompatible);
            CaptureWindow(form, configImagePath);
            form.PreviewEngine(TranslatorKind.Google);

            var lines = new List<string>
            {
                $"OK {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"分片与语言码：{primitives}",
                $"配置迁移与读写：{migration}",
                $"引擎数量：{EngineRegistry.All.Count}",
                $"设置窗口：{form.Width}x{form.Height}",
                $"窗口截图：{imagePath}（免配置引擎）、{configImagePath}（需配置引擎）"
            };
            File.WriteAllLines(reportPath, lines, new UTF8Encoding(false));
            Console.WriteLine(string.Join(Environment.NewLine, lines));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(reportPath, ex.ToString(), new UTF8Encoding(false));
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Environment.SetEnvironmentVariable("STRANSLATE_LITE_HOME", originalHome);
            try
            {
                if (Directory.Exists(tempHome))
                    Directory.Delete(tempHome, recursive: true);
            }
            catch
            {
                // 临时目录清理失败不影响自检结论。
            }
        }
    }

    /// <summary>
    /// 校验分片不丢字符、不切断代理对、不超限，以及语言码归一化。
    /// </summary>
    private static string VerifyEnginePrimitives()
    {
        var text = string.Concat(Enumerable.Repeat("敏捷的棕色狐狸跳过懒狗。abc ", 120));
        var chunks = TextChunker.Split(text, 100);
        Assert(chunks.Count > 1, "按码点分片没有生效。");
        Assert(chunks.All(chunk => chunk.Length <= 100), "按码点分片存在超限片段。");
        Assert(string.Concat(chunks) == text, "按码点分片拼接后与原文本不一致。");

        var emoji = string.Concat(Enumerable.Repeat("🙂中", 200));
        var byteChunks = TextChunker.SplitByUtf8Bytes(emoji, 64);
        Assert(byteChunks.All(chunk => Encoding.UTF8.GetByteCount(chunk) <= 64), "按字节分片存在超限片段。");
        Assert(string.Concat(byteChunks) == emoji, "按字节分片拼接后与原文本不一致。");
        Assert(byteChunks.All(chunk => !char.IsLowSurrogate(chunk[0])), "按字节分片把代理对切开了。");

        Assert(LanguageCodes.Normalize("zh-hans") == "zh-CN", "zh-hans 未归一化为 zh-CN。");
        Assert(LanguageCodes.Normalize("ZH-HANT") == "zh-TW", "zh-hant 未归一化为 zh-TW。");
        Assert(LanguageCodes.IsChinese("zh-TW"), "繁体中文识别失败。");
        Assert(LanguageCodes.Base("en-US") == "en", "en-US 的基码不是 en。");

        return $"通过（码点 {chunks.Count} 段、字节 {byteChunks.Count} 段、语言码归一化）";
    }

    /// <summary>
    /// 写入一份 v1 配置，验证 OpenAi 迁移到 Services、保存后能原样读回。
    /// </summary>
    private static string VerifySettingsRoundTrip()
    {
        const string legacyJson = """
        {
          "Version": 1,
          "Translator": "OpenAiCompatible",
          "TargetLanguage": "auto",
          "OpenAi": {
            "Endpoint": "https://api.deepseek.com/v1",
            "ApiKey": "sk-legacy",
            "Model": "deepseek-chat"
          }
        }
        """;

        AppPaths.EnsureDataDirectory();
        File.WriteAllText(AppPaths.SettingsFile, legacyJson, new UTF8Encoding(false));

        var store = new SettingsStore();
        var loaded = store.Load();
        var migrated = loaded.GetService(TranslatorKind.OpenAiCompatible);
        Assert(migrated.ApiKey == "sk-legacy", "v1 的 ApiKey 未迁移到 Services。");
        Assert(migrated.Endpoint == "https://api.deepseek.com/v1", "v1 的 Endpoint 未迁移到 Services。");
        Assert(migrated.Model == "deepseek-chat", "v1 的 Model 未迁移到 Services。");
        Assert(loaded.Translator == TranslatorKind.OpenAiCompatible, "引擎选择在读取后发生变化。");

        loaded.SetService(TranslatorKind.Ollama, new ServiceOptions
        {
            Endpoint = "http://127.0.0.1:11434",
            Model = "qwen2.5:7b"
        });
        store.Save(loaded);

        var saved = File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8);
        Assert(!saved.Contains("\"OpenAi\"", StringComparison.Ordinal), "旧字段 OpenAi 仍被写回配置文件。");

        var reloaded = store.Load();
        Assert(reloaded.Version == 2, "保存后配置版本不是 2。");
        Assert(reloaded.GetService(TranslatorKind.Ollama).Model == "qwen2.5:7b", "新增引擎配置未持久化。");
        Assert(reloaded.GetService(TranslatorKind.OpenAiCompatible).ApiKey == "sk-legacy", "迁移后的配置未能读回。");

        return "通过（v1 → v2，含旧字段清理）";
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    /// <summary>把窗口渲染成图片，便于在无桌面会话的机器上检查布局。</summary>
    private static void CaptureWindow(Form form, string path)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(0, 0);
        form.ShowInTaskbar = false;
        form.Show();
        try
        {
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(path, ImageFormat.Png);
        }
        finally
        {
            form.Hide();
        }
    }
}