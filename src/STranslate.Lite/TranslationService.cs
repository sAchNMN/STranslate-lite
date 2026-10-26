using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace STranslate.Lite;

internal sealed record TranslationResult(
    string Text,
    string SourceLanguage,
    string TargetLanguage,
    string TranslatorName);

/// <summary>
/// 翻译调度：默认使用无需 API Key 的 Google 免费接口，也可以切换到 OpenAI 兼容接口。
/// </summary>
internal sealed class TranslationService : IDisposable
{
    private const string GoogleEndpoint = "https://translate.googleapis.com/translate_a/single";
    private const int GoogleGetTextLimit = 1800;

    private readonly HttpClient _httpClient;

    public TranslationService()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var targetLanguage = LanguageUtil.ResolveTargetLanguage(text, settings.TargetLanguage);
        try
        {
            return settings.Translator == TranslatorKind.OpenAiCompatible
                ? await TranslateWithOpenAiAsync(text, targetLanguage, settings.OpenAi, cancellationToken)
                    .ConfigureAwait(false)
                : await TranslateWithGoogleAsync(text, targetLanguage, cancellationToken)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("翻译请求超时，请检查网络或代理。");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("网络请求失败，请检查网络连接或系统代理。", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("翻译接口返回了无法解析的数据。", ex);
        }
    }

    private async Task<TranslationResult> TranslateWithGoogleAsync(
        string text,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        using var request = BuildGoogleRequest(text, targetLanguage);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateHttpStatusException(response.StatusCode, "Google");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 ||
            root[0].ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Google 返回的数据结构不符合预期。");
        }

        var builder = new StringBuilder();
        foreach (var segment in root[0].EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0)
                continue;

            var translated = segment[0];
            if (translated.ValueKind == JsonValueKind.String)
                builder.Append(translated.GetString());
        }

        var translatedText = builder.ToString().Trim();
        if (translatedText.Length == 0)
            throw new InvalidOperationException("Google 未返回翻译结果。");

        var sourceLanguage = root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String
            ? root[2].GetString() ?? "auto"
            : "auto";

        return new TranslationResult(
            translatedText,
            sourceLanguage,
            targetLanguage,
            "Google");
    }

    private static HttpRequestMessage BuildGoogleRequest(string text, string targetLanguage)
    {
        if (text.Length <= GoogleGetTextLimit)
        {
            var url = $"{GoogleEndpoint}?client=gtx&sl=auto&tl={Uri.EscapeDataString(targetLanguage)}" +
                      $"&dt=t&q={Uri.EscapeDataString(text)}";
            return new HttpRequestMessage(HttpMethod.Get, url);
        }

        // 较长文本使用 POST，避免 URL 超过各层代理限制。
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client"] = "gtx",
            ["sl"] = "auto",
            ["tl"] = targetLanguage,
            ["dt"] = "t",
            ["q"] = text
        });

        return new HttpRequestMessage(HttpMethod.Post, GoogleEndpoint)
        {
            Content = body
        };
    }

    private async Task<TranslationResult> TranslateWithOpenAiAsync(
        string text,
        string targetLanguage,
        OpenAiCompatibleOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("OpenAI 兼容接口尚未配置 API Key。");
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("OpenAI 兼容接口地址无效。");
        }

        var targetName = LanguageUtil.GetDisplayName(targetLanguage);
        var payload = new
        {
            model = options.Model,
            temperature = 0,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = $"You are a precise translation engine. Translate the user's text into {targetName}. " +
                              "Output only the translation, without explanations, quotes, or Markdown."
                },
                new { role = "user", content = text }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {options.ApiKey}");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateHttpStatusException(response.StatusCode, "OpenAI 兼容接口");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("OpenAI 兼容接口返回的数据结构不符合预期。");
        }

        var translatedText = content.GetString()?.Trim();
        if (string.IsNullOrEmpty(translatedText))
            throw new InvalidOperationException("OpenAI 兼容接口未返回翻译结果。");

        return new TranslationResult(
            translatedText,
            "auto",
            targetLanguage,
            "OpenAI");
    }

    private static InvalidOperationException CreateHttpStatusException(HttpStatusCode statusCode, string provider)
    {
        var message = statusCode switch
        {
            HttpStatusCode.Unauthorized => $"{provider} 鉴权失败，请检查 API Key。",
            HttpStatusCode.Forbidden => $"{provider} 拒绝请求，可能被网络环境或服务端策略拦截。",
            HttpStatusCode.TooManyRequests => $"{provider} 请求过于频繁（HTTP 429），请稍后再试。",
            _ => $"{provider} 返回 HTTP {(int)statusCode} ({statusCode})。"
        };

        return new InvalidOperationException(message);
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
        return languageCode.Trim().ToLowerInvariant() switch
        {
            "auto" => "自动",
            "zh" or "zh-cn" or "zh-hans" => "简体中文",
            "zh-tw" or "zh-hant" => "繁體中文",
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
            _ => languageCode
        };
    }
}