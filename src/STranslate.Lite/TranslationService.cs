using System.Net;
using System.Net.Http;
using STranslate.Lite.Engines;

namespace STranslate.Lite;

internal sealed record TranslationResult(
    string Text,
    string SourceLanguage,
    string TargetLanguage,
    string TranslatorName,
    string? Preprocessed = null);

/// <summary>
/// 翻译调度：把请求交给 EngineRegistry 中选中的引擎，并统一归一化超时与网络错误。
/// </summary>
internal sealed class TranslationService : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _httpClient;

    public TranslationService()
    {
        _httpClient = CreateClient();
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var engine = EngineRegistry.Get(settings.Translator);
        var targetLanguage = LanguageUtil.ResolveTargetLanguage(text, settings.TargetLanguage);
        var context = new EngineContext(_httpClient, settings.GetService(settings.Translator));
        var plan = settings.SplitCodeIdentifiers ? CodeTextPreprocessor.CreatePlan(text) : CodeTextPlan.None;

        try
        {
            var result = await engine.TranslateAsync(text, targetLanguage, context, cancellationToken)
                .ConfigureAwait(false);

            var retried = await RetryWithSplitIdentifiersAsync(
                    engine,
                    plan,
                    targetLanguage,
                    context,
                    result,
                    cancellationToken)
                .ConfigureAwait(false);

            var detected = retried?.DetectedSource ?? result.DetectedSource;

            return new TranslationResult(
                retried?.Text ?? result.Text,
                string.IsNullOrWhiteSpace(detected)
                    ? "auto"
                    : LanguageCodes.Normalize(detected),
                LanguageCodes.Normalize(targetLanguage),
                engine.Name,
                retried?.Rewritten);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{engine.Name} 翻译超时（{RequestTimeout.TotalSeconds:0} 秒），请检查网络或代理。");
        }
        catch (HttpRequestException ex)
        {
            throw DescribeNetworkFailure(engine, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException($"{engine.Name} 返回了无法解析的数据。", ex);
        }
    }

    /// <summary>
    /// 首次译文原样回显了标识符（current_thread → current_thread）时，改用拆分后的文本重试一次。
    /// 只有重试确实拿到了译文才替换首次结果，避免把本来可用的译文换掉。
    /// </summary>
    private static async Task<RetryResult?> RetryWithSplitIdentifiersAsync(
        TranslateEngine engine,
        CodeTextPlan plan,
        string targetLanguage,
        EngineContext context,
        EngineResult first,
        CancellationToken cancellationToken)
    {
        if (!plan.Applied || !LooksUntranslated(plan, targetLanguage, first.Text))
            return null;

        EngineResult retry;
        try
        {
            retry = await engine.TranslateAsync(plan.Rewritten, targetLanguage, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 重试只是补救手段，失败时保留首次结果，不额外打扰用户。
            Log.Info($"拆分代码标识符后重试失败，保留首次译文：{ex.Message}");
            return null;
        }

        if (LooksUntranslated(plan, targetLanguage, retry.Text))
            return null;

        Log.Info("译文原样回显代码标识符，已用拆分后的文本重新翻译。");

        return new RetryResult(
            CodeTextPreprocessor.StripEcho(plan, retry.Text),
            plan.Rewritten,
            retry.DetectedSource);
    }

    /// <summary>
    /// 判断译文是否根本没有翻译：与原文完全一致，或目标语言是中日韩却一个对应文字都没有、
    /// 且原文的标识符被原样带回。
    /// </summary>
    private static bool LooksUntranslated(CodeTextPlan plan, string targetLanguage, string translated)
    {
        var result = translated.Trim();
        if (result.Length == 0)
            return false;

        if (string.Equals(Collapse(result), Collapse(plan.Original), StringComparison.OrdinalIgnoreCase))
            return true;

        return IsCjkTarget(targetLanguage) &&
               !CodeTextPreprocessor.ContainsCjk(result) &&
               CodeTextPreprocessor.ContainsUntouched(plan, result);
    }

    private static bool IsCjkTarget(string targetLanguage) =>
        targetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
        targetLanguage.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ||
        targetLanguage.StartsWith("ko", StringComparison.OrdinalIgnoreCase);

    private static string Collapse(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
                builder.Append(character);
        }

        return builder.ToString();
    }

    private sealed record RetryResult(string Text, string Rewritten, string? DetectedSource);

    /// <summary>
    /// 使用给定引擎与配置翻译一句固定英文，供设置窗口的“测试”按钮和命令行诊断复用。
    /// </summary>
    public static async Task<EngineResult> TestAsync(
        TranslatorKind kind,
        ServiceOptions service,
        string targetLanguage,
        string testText,
        CancellationToken cancellationToken)
    {
        var engine = EngineRegistry.Get(kind);
        using var client = CreateClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            return await engine.TranslateAsync(
                    testText,
                    targetLanguage,
                    new EngineContext(client, service),
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{engine.Name} 测试超时（{RequestTimeout.TotalSeconds:0} 秒）。");
        }
        catch (HttpRequestException ex)
        {
            throw DescribeNetworkFailure(engine, ex);
        }
    }

    private static InvalidOperationException DescribeNetworkFailure(
        TranslateEngine engine,
        HttpRequestException exception)
    {
        for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is System.Security.Authentication.AuthenticationException)
            {
                return new InvalidOperationException(
                    $"{engine.Name} 的 TLS 证书校验失败，服务端证书可能已过期或被网络中间设备拦截，请改用其它引擎。",
                    exception);
            }

            if (inner is System.Net.Sockets.SocketException socket &&
                socket.SocketErrorCode is System.Net.Sockets.SocketError.ConnectionRefused
                    or System.Net.Sockets.SocketError.HostNotFound
                    or System.Net.Sockets.SocketError.HostUnreachable
                    or System.Net.Sockets.SocketError.TimedOut)
            {
                return new InvalidOperationException(
                    $"{engine.Name} 无法连接到服务地址（{socket.SocketErrorCode}），" +
                    "请确认本地 / 自建服务已启动，并检查地址与端口。",
                    exception);
            }
        }

        return new InvalidOperationException(
            $"{engine.Name} 网络请求失败，请检查网络连接或系统代理。", exception);
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };

        var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36");
        return client;
    }

    public void Dispose() => _httpClient.Dispose();
}

internal static class LanguageUtil
{
    public static string ResolveTargetLanguage(string text, string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) &&
            !string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return configured.Trim();
        }

        var hasKanaOrHangul = false;
        var hasHan = false;

        foreach (var character in text)
        {
            if (character is >= '\u3040' and <= '\u30FF' or >= '\uAC00' and <= '\uD7AF')
            {
                hasKanaOrHangul = true;
                break;
            }

            if (character is >= '\u4E00' and <= '\u9FFF')
                hasHan = true;
        }

        // 中文默认翻成英文；日/韩文以及其它语言默认翻成简体中文。
        return hasHan && !hasKanaOrHangul ? "en" : "zh-CN";
    }

    public static string GetDisplayName(string languageCode)
    {
        return LanguageCodes.Normalize(languageCode) switch
        {
            "auto" => "自动",
            "zh-CN" => "简体中文",
            "zh-TW" => "繁體中文",
            "en" => "English",
            "ja" => "日本語",
            "ko" => "한국어",
            "fr" => "Français",
            "de" => "Deutsch",
            "es" => "Español",
            "ru" => "Русский",
            "pt" => "Português",
            "it" => "Italiano",
            "ar" => "العربية",
            "vi" => "Tiếng Việt",
            "th" => "ไทย",
            "id" => "Bahasa Indonesia",
            "ms" => "Bahasa Melayu",
            "tr" => "Türkçe",
            "hi" => "हिन्दी",
            "nl" => "Nederlands",
            "pl" => "Polski",
            "uk" => "Українська",
            "fa" => "فارسی",
            "he" => "עברית",
            "sv" => "Svenska",
            "da" => "Dansk",
            "fi" => "Suomi",
            "no" => "Norsk",
            "cs" => "Čeština",
            "el" => "Ελληνικά",
            "hu" => "Magyar",
            "ro" => "Română",
            "sk" => "Slovenčina",
            "bg" => "Български",
            "hr" => "Hrvatski",
            "sr" => "Српски",
            "sl" => "Slovenščina",
            "et" => "Eesti",
            "lv" => "Latviešu",
            "lt" => "Lietuvių",
            "bn" => "বাংলা",
            "ta" => "தமிழ்",
            "te" => "తెలుగు",
            "ur" => "اردو",
            "fil" => "Filipino",
            "my" => "မြန်မာ",
            "km" => "ខ្មែរ",
            "lo" => "ລາວ",
            "mn" => "Монгол",
            "uz" => "Oʻzbek",
            "kk" => "Қазақ",
            "az" => "Azərbaycan",
            "ka" => "ქართული",
            "hy" => "Հայերեն",
            "ne" => "नेपाली",
            "si" => "සිංහල",
            "sw" => "Kiswahili",
            "am" => "አማርኛ",
            _ => languageCode
        };
    }
}