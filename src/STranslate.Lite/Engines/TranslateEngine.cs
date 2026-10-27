using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace STranslate.Lite.Engines;

/// <summary>引擎翻译结果。DetectedSource 在引擎能识别源语言时给出。</summary>
internal sealed record EngineResult(string Text, string? DetectedSource = null);

/// <summary>一次翻译调用可用的运行时依赖。</summary>
internal sealed class EngineContext
{
    public EngineContext(HttpClient http, ServiceOptions service)
    {
        Http = http;
        Service = service;
    }

    public HttpClient Http { get; }

    public ServiceOptions Service { get; }

    public string Endpoint(string? fallback) =>
        string.IsNullOrWhiteSpace(Service.Endpoint) ? fallback ?? string.Empty : Service.Endpoint.Trim();

    public string Model(string? fallback) =>
        string.IsNullOrWhiteSpace(Service.Model) ? fallback ?? string.Empty : Service.Model.Trim();
}

/// <summary>
/// 翻译引擎基类。每个实现只负责把文本发给供应商并还原译文，
/// 语言映射、分片、错误归一由基类与 TranslationService 统一处理。
/// </summary>
internal abstract class TranslateEngine
{
    public abstract TranslatorKind Kind { get; }

    /// <summary>设置界面与浮窗标题展示的名称。</summary>
    public abstract string Name { get; }

    /// <summary>设置界面中的分组。</summary>
    public abstract string Group { get; }

    /// <summary>是否需要填写服务地址。</summary>
    public virtual bool SupportsEndpoint => false;

    /// <summary>是否需要填写 API Key。</summary>
    public virtual bool RequiresApiKey => false;

    /// <summary>是否支持自定义模型名。</summary>
    public virtual bool SupportsModel => false;

    /// <summary>地址留空时使用的默认值（自建服务为 localhost）。</summary>
    public virtual string? DefaultEndpoint => null;

    /// <summary>模型留空时使用的默认值。</summary>
    public virtual string? DefaultModel => null;

    /// <summary>设置界面的补充说明。</summary>
    public virtual string? ConfigHint => null;

    /// <summary>是否支持传入具体源语言；不支持的引擎只接受自动识别。</summary>
    public virtual bool SupportsSourceLanguage => false;

    public abstract Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken);

    protected InvalidOperationException NoResult(string? detail = null) =>
        new(string.IsNullOrWhiteSpace(detail) ? $"{Name} 未返回翻译结果。" : $"{Name} 未返回翻译结果：{detail}");

    protected static string RequireText(string? value, TranslateEngine engine)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw engine.NoResult();

        return value.Trim();
    }
}

/// <summary>HTTP / JSON 小工具，统一各引擎的请求与错误处理。</summary>
internal static class EngineHttp
{
    public static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    public static FormUrlEncodedContent FormContent(IEnumerable<KeyValuePair<string, string>> fields) =>
        new(fields);

    public static async Task<JsonDocument> SendAsync(
        HttpClient http,
        HttpRequestMessage request,
        TranslateEngine engine,
        CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw StatusError(engine, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{engine.Name} 返回了无法解析的数据。", ex);
        }
    }

    public static async Task<string> SendTextAsync(
        HttpClient http,
        HttpRequestMessage request,
        TranslateEngine engine,
        CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw StatusError(engine, response.StatusCode);

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public static InvalidOperationException StatusError(TranslateEngine engine, HttpStatusCode status) =>
        new(status switch
        {
            HttpStatusCode.Unauthorized => $"{engine.Name} 鉴权失败，请检查 API Key。",
            HttpStatusCode.Forbidden => $"{engine.Name} 拒绝了请求（HTTP 403），可能被网络环境或服务端策略拦截。",
            HttpStatusCode.TooManyRequests => $"{engine.Name} 请求过于频繁（HTTP 429），请稍后再试。",
            HttpStatusCode.NotFound => $"{engine.Name} 接口地址不存在（HTTP 404），请检查服务地址。",
            HttpStatusCode.RequestEntityTooLarge => $"{engine.Name} 提示请求内容过大。",
            _ => $"{engine.Name} 返回 HTTP {(int)status} ({status})。"
        });

    public static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>语言代码归一化：内部统一使用 zh-CN / zh-TW / en / ja 这类写法。</summary>
internal static class LanguageCodes
{
    public static string Normalize(string? code)
    {
        var value = (code ?? string.Empty).Trim().Replace('_', '-').ToLowerInvariant();
        return value switch
        {
            "" or "auto" => "auto",
            "zh" or "zh-cn" or "zh-hans" or "zh-chs" or "chs" => "zh-CN",
            "zh-tw" or "zh-hk" or "zh-mo" or "zh-hant" or "zh-cht" or "cht" => "zh-TW",
            "en-us" or "en-gb" or "en" => "en",
            "ja" or "ja-jp" => "ja",
            "ko" or "ko-kr" => "ko",
            "pt-br" or "pt-pt" or "pt" => "pt",
            _ => value
        };
    }

    public static bool IsTraditionalChinese(string code) => Normalize(code) == "zh-TW";

    public static bool IsChinese(string code)
    {
        var normalized = Normalize(code);
        return normalized is "zh-CN" or "zh-TW";
    }

    public static string Base(string code)
    {
        var normalized = Normalize(code);
        var index = normalized.IndexOf('-');
        return index < 0 ? normalized : normalized[..index];
    }

    /// <summary>给大模型提示用的中文语言名。</summary>
    public static string ChineseName(string code) => Normalize(code) switch
    {
        "zh-CN" => "简体中文",
        "zh-TW" => "繁體中文",
        "en" => "英文",
        "ja" => "日文",
        "ko" => "韩文",
        "fr" => "法文",
        "de" => "德文",
        "es" => "西班牙文",
        "ru" => "俄文",
        "pt" => "葡萄牙文",
        "it" => "意大利文",
        "ar" => "阿拉伯文",
        "vi" => "越南文",
        "th" => "泰文",
        "id" => "印尼文",
        "ms" => "马来文",
        "tr" => "土耳其文",
        "hi" => "印地文",
        "nl" => "荷兰文",
        "pl" => "波兰文",
        "uk" => "乌克兰文",
        "fa" => "波斯文",
        "he" => "希伯来文",
        "sv" => "瑞典文",
        "da" => "丹麦文",
        "fi" => "芬兰文",
        "no" => "挪威文",
        "cs" => "捷克文",
        "el" => "希腊文",
        "hu" => "匈牙利文",
        "ro" => "罗马尼亚文",
        "sk" => "斯洛伐克文",
        "bg" => "保加利亚文",
        "hr" => "克罗地亚文",
        "sr" => "塞尔维亚文",
        "sl" => "斯洛文尼亚文",
        "et" => "爱沙尼亚文",
        "lv" => "拉脱维亚文",
        "lt" => "立陶宛文",
        "bn" => "孟加拉文",
        "ta" => "泰米尔文",
        "te" => "泰卢固文",
        "ur" => "乌尔都文",
        "fil" => "菲律宾文",
        "my" => "缅甸文",
        "km" => "高棉文",
        "lo" => "老挝文",
        "mn" => "蒙古文",
        "uz" => "乌兹别克文",
        "kk" => "哈萨克文",
        "az" => "阿塞拜疆文",
        "ka" => "格鲁吉亚文",
        "hy" => "亚美尼亚文",
        "ne" => "尼泊尔文",
        "si" => "僧伽罗文",
        "sw" => "斯瓦希里文",
        "am" => "阿姆哈拉文",
        _ => code
    };

    /// <summary>不带源语言检测能力、且只认少数语言的引擎使用。</summary>
    public static string AtLeast(string code, IReadOnlyDictionary<string, string> table, TranslateEngine engine)
    {
        var normalized = Normalize(code);
        if (table.TryGetValue(normalized, out var mapped))
            return mapped;
        if (table.TryGetValue(Base(normalized), out mapped))
            return mapped;

        throw new InvalidOperationException($"{engine.Name} 不支持目标语言 {code}。");
    }
}

/// <summary>极简本地语种识别，仅用于不支持自动检测的引擎（MyMemory / Apertium / Youdao）。</summary>
internal static class LanguageDetector
{
    public static string? Detect(string text)
    {
        var kana = false;
        var hangul = false;
        var han = false;
        var cyrillic = false;
        var arabic = false;
        var thai = false;
        var hebrew = false;
        var devanagari = false;
        var latin = false;

        foreach (var ch in text)
        {
            switch (ch)
            {
                case >= '\u3040' and <= '\u30FF':
                    kana = true;
                    break;
                case >= '\uAC00' and <= '\uD7AF':
                    hangul = true;
                    break;
                case >= '\u4E00' and <= '\u9FFF':
                    han = true;
                    break;
                case >= '\u0400' and <= '\u04FF':
                    cyrillic = true;
                    break;
                case >= '\u0600' and <= '\u06FF':
                    arabic = true;
                    break;
                case >= '\u0E00' and <= '\u0E7F':
                    thai = true;
                    break;
                case >= '\u0590' and <= '\u05FF':
                    hebrew = true;
                    break;
                case >= '\u0900' and <= '\u097F':
                    devanagari = true;
                    break;
                case >= 'a' and <= 'z':
                case >= 'A' and <= 'Z':
                    latin = true;
                    break;
            }
        }

        if (kana) return "ja";
        if (hangul) return "ko";
        if (han) return "zh-CN";
        if (thai) return "th";
        if (arabic) return "ar";
        if (cyrillic) return "ru";
        if (hebrew) return "he";
        if (devanagari) return "hi";
        if (latin) return "en";
        return null;
    }
}

/// <summary>按码点或 UTF-8 字节安全切分长文本，优先在换行 / 空格 / 句末标点处断开。</summary>
internal static class TextChunker
{
    private const string SoftBreaks = "\n\r \t。！？；，、.!?;,";

    public static IReadOnlyList<string> Split(string text, int maxCodepoints)
    {
        if (string.IsNullOrEmpty(text) || maxCodepoints <= 0)
            return [text];

        var result = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var index = start;
            var codepoints = 0;
            while (index < text.Length && codepoints < maxCodepoints)
            {
                index += char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
                codepoints++;
            }

            if (index >= text.Length)
            {
                result.Add(text[start..]);
                break;
            }

            var cut = FindSoftBreak(text, start, index);
            if (cut <= start)
                cut = index;

            // 不把代理对切成两半。
            if (cut < text.Length && char.IsLowSurrogate(text[cut]))
                cut--;

            if (cut <= start)
                cut = index;

            result.Add(text[start..cut]);
            start = cut;
        }

        return result;
    }

    public static IReadOnlyList<string> SplitByUtf8Bytes(string text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text) || maxBytes <= 0)
            return [text];

        var result = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var bytes = 0;
            var index = start;
            while (index < text.Length)
            {
                var size = char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
                var byteCount = Encoding.UTF8.GetByteCount(text.AsSpan(index, size));
                if (bytes + byteCount > maxBytes)
                    break;

                bytes += byteCount;
                index += size;
            }

            if (index >= text.Length)
            {
                result.Add(text[start..]);
                break;
            }

            if (index == start)
                index = Math.Min(text.Length, start + 1);

            var cut = FindSoftBreak(text, start, index);
            if (cut <= start)
                cut = index;

            var slice = text[start..cut];
            if (Encoding.UTF8.GetByteCount(slice) > maxBytes)
                cut = Math.Max(start + 1, cut - 1);

            result.Add(text[start..cut]);
            start = cut;
        }

        return result;
    }

    private static int FindSoftBreak(string text, int start, int hardEnd)
    {
        for (var index = hardEnd - 1; index > start; index--)
        {
            if (SoftBreaks.IndexOf(text[index]) >= 0)
                return index + 1;
        }

        return -1;
    }
}