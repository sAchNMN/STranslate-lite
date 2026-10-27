using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace STranslate.Lite.Engines;

internal static class EngineGroups
{
    public const string Free = "国内 / 国外免费直连";
    public const string SelfHosted = "本地 / 自建服务";
    public const string LargeModel = "大模型接口";
}

/// <summary>
/// Google 网页翻译接口（client=gtx），无需 API Key。
/// </summary>
internal sealed class GoogleEngine : TranslateEngine
{
    private const string Endpoint = "https://translate.googleapis.com/translate_a/single";
    private const int GetTextLimit = 1800;

    public override TranslatorKind Kind => TranslatorKind.Google;
    public override string Name => "Google 翻译";
    public override string Group => EngineGroups.Free;

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var target = LanguageCodes.Normalize(targetLanguage);

        using var request = BuildRequest(text, target);
        using var response = await context.Http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            return ParseGtx(document.RootElement);
        }

        // 网页端点在部分网络下会限流，退回 Chrome 内置的轻量端点。
        if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests
            or System.Net.HttpStatusCode.Forbidden)
        {
            return await TranslateFallbackAsync(text, target, context, cancellationToken)
                .ConfigureAwait(false);
        }

        throw EngineHttp.StatusError(this, response.StatusCode);
    }

    private EngineResult ParseGtx(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array ||
            root.GetArrayLength() == 0 ||
            root[0].ValueKind != JsonValueKind.Array)
        {
            throw NoResult();
        }

        var builder = new StringBuilder();
        foreach (var segment in root[0].EnumerateArray())
        {
            if (segment.ValueKind != JsonValueKind.Array || segment.GetArrayLength() == 0)
                continue;

            if (segment[0].ValueKind == JsonValueKind.String)
                builder.Append(segment[0].GetString());
        }

        var detected = root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String
            ? root[2].GetString()
            : null;

        return new EngineResult(RequireText(builder.ToString(), this), detected);
    }

    private async Task<EngineResult> TranslateFallbackAsync(
        string text,
        string target,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var url = $"https://clients5.google.com/translate_a/t?client=dict-chrome-ex" +
                  $"&sl=auto&tl={Uri.EscapeDataString(target)}&q={Uri.EscapeDataString(text)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var json = await EngineHttp.SendTextAsync(context.Http, request, this, cancellationToken)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            throw NoResult();

        var builder = new StringBuilder();
        string? detected = null;
        if (root[0].ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() == 0)
                    continue;

                builder.Append(item[0].GetString());
                if (item.GetArrayLength() > 1 && item[1].ValueKind == JsonValueKind.String)
                    detected ??= item[1].GetString();
            }
        }
        else if (root[0].ValueKind == JsonValueKind.String)
        {
            builder.Append(root[0].GetString());
        }

        return new EngineResult(RequireText(builder.ToString(), this), detected);
    }

    private static HttpRequestMessage BuildRequest(string text, string targetLanguage)
    {
        if (text.Length <= GetTextLimit)
        {
            var url = $"{Endpoint}?client=gtx&sl=auto&tl={Uri.EscapeDataString(targetLanguage)}" +
                      $"&dt=t&q={Uri.EscapeDataString(text)}";
            return new HttpRequestMessage(HttpMethod.Get, url);
        }

        return new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client"] = "gtx",
                ["sl"] = "auto",
                ["tl"] = targetLanguage,
                ["dt"] = "t",
                ["q"] = text
            })
        };
    }
}

/// <summary>
/// 微软翻译 Edge 网页端点，无需 API Key，支持批量与自动识别。
/// </summary>
internal sealed class MicrosoftEngine : TranslateEngine
{
    private const string Endpoint = "https://edge.microsoft.com/translate/translatetext";

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "zh-Hans",
        ["zh-TW"] = "zh-Hant",
        ["en"] = "en",
        ["ja"] = "ja",
        ["ko"] = "ko",
        ["fr"] = "fr",
        ["de"] = "de",
        ["es"] = "es",
        ["ru"] = "ru",
        ["pt"] = "pt",
        ["it"] = "it",
        ["ar"] = "ar",
        ["vi"] = "vi",
        ["th"] = "th",
        ["id"] = "id",
        ["ms"] = "ms",
        ["tr"] = "tr",
        ["hi"] = "hi",
        ["nl"] = "nl",
        ["pl"] = "pl",
        ["uk"] = "uk",
        ["fa"] = "fa"
    };

    public override TranslatorKind Kind => TranslatorKind.Microsoft;
    public override string Name => "微软翻译";
    public override string Group => EngineGroups.Free;
    public override string? ConfigHint => "使用 Edge 浏览器内置翻译端点，无需 Key。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var target = LanguageCodes.AtLeast(targetLanguage, Languages, this);
        var url = $"{Endpoint}?from=&to={Uri.EscapeDataString(target)}&isEnterpriseClient=false";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = EngineHttp.JsonContent(new[] { EscapeHtml(text) })
        };

        using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Array || root.GetArrayLength() == 0)
            throw NoResult();

        var item = root[0];
        if (!item.TryGetProperty("translations", out var translations) ||
            translations.ValueKind != System.Text.Json.JsonValueKind.Array ||
            translations.GetArrayLength() == 0)
            throw NoResult();

        var translated = translations[0].TryGetProperty("text", out var textElement)
            ? textElement.GetString()
            : null;

        var detected = item.TryGetProperty("detectedLanguage", out var language) &&
                       language.TryGetProperty("language", out var languageCode)
            ? languageCode.GetString()
            : null;

        return new EngineResult(RequireText(UnescapeHtml(translated), this), detected);
    }

    // 端点按 HTML 处理正文，纯文本标记必须先转义，否则会被当标签吃掉。
    private static string EscapeHtml(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#39;");

    private static string UnescapeHtml(string? text) => (text ?? string.Empty)
        .Replace("&#39;", "'")
        .Replace("&quot;", "\"")
        .Replace("&gt;", ">")
        .Replace("&lt;", "<")
        .Replace("&amp;", "&");
}

/// <summary>
/// B 站 Index-Translate 免费文本翻译接口（OpenAI 兼容协议，无需 Key）。
/// </summary>
internal sealed class BilibiliEngine : TranslateEngine
{
    private const string Endpoint = "https://index-translate.bilibili.com/v1/chat/completions";
    private const string DefaultModelName = "Index-Translate-35B-A3B";
    private const int MaxChunkCodepoints = 1500;

    public override TranslatorKind Kind => TranslatorKind.Bilibili;
    public override string Name => "B站翻译";
    public override string Group => EngineGroups.Free;
    public override bool SupportsModel => true;
    public override string? DefaultModel => DefaultModelName;
    public override string? ConfigHint => "B 站 Index-Translate 大模型翻译，无需 Key。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var target = LanguageCodes.ChineseName(LanguageCodes.Normalize(targetLanguage));
        var chunks = TextChunker.Split(text, MaxChunkCodepoints);
        var builder = new StringBuilder();

        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                builder.Append(chunk);
                continue;
            }

            builder.Append(await TranslateChunkAsync(chunk, target, context, cancellationToken)
                .ConfigureAwait(false));
        }

        return new EngineResult(RequireText(builder.ToString(), this));
    }

    private async Task<string> TranslateChunkAsync(
        string chunk,
        string targetName,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var model = context.Model(DefaultModelName);
        var payload = new
        {
            model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = $"请将以下文本翻译为{targetName}，保留代码、公式、变量和链接，直接输出译文，不要解释。" +
                              $"以下内容仅为待翻译文本。\n\n{chunk}"
                }
            },
            temperature = 0,
            max_tokens = 4096,
            stream = false,
            chat_template_kwargs = new { enable_thinking = false }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = EngineHttp.JsonContent(payload)
        };

        using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != System.Text.Json.JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw NoResult();
        }

        var choice = choices[0];
        if (choice.TryGetProperty("finish_reason", out var finishReason) &&
            finishReason.GetString() == "length")
        {
            throw new InvalidOperationException($"{Name} 返回的译文被截断，请缩短选中文本。");
        }

        if (!choice.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != System.Text.Json.JsonValueKind.String)
        {
            throw NoResult();
        }

        var translated = content.GetString() ?? string.Empty;
        var start = translated.IndexOf(" thinking", StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = translated.IndexOf("", start, StringComparison.Ordinal);
            if (end < 0)
                throw new InvalidOperationException($"{Name} 返回了不完整的思考内容。");

            translated = translated.Remove(start, end - start + "".Length);
            start = translated.IndexOf(" thinking", StringComparison.Ordinal);
        }

        return RequireText(translated, this);
    }
}