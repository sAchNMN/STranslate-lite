using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace STranslate.Lite.Engines;

/// <summary>Lara 官方网页翻译（X-Lara-Client 匿名调用），无需 API Key。</summary>
internal sealed class LaraEngine : TranslateEngine
{
    private const string Endpoint = "https://webapi.laratranslate.com/translate/segmented";
    private const int MaxChunkCodepoints = 1000;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "zh-CN",
        ["zh-TW"] = "zh-TW",
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

    public override TranslatorKind Kind => TranslatorKind.Lara;
    public override string Name => "Lara 翻译";
    public override string Group => EngineGroups.Free;

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var target = LanguageCodes.AtLeast(targetLanguage, Languages, this);
        var chunks = TextChunker.Split(text, MaxChunkCodepoints);
        var builder = new StringBuilder();
        string? detected = null;

        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                builder.Append(chunk);
                continue;
            }

            var payload = new
            {
                q = chunk,
                source = string.Empty,
                target,
                source_hint = string.Empty,
                style = "faithful",
                content_type = "text/plain",
                adapt_to = Array.Empty<string>(),
                glossaries = Array.Empty<string>(),
                instructions = Array.Empty<string>()
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = EngineHttp.JsonContent(payload)
            };
            request.Headers.TryAddWithoutValidation("X-Lara-Client", "Webapp");

            using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
                .ConfigureAwait(false);

            var root = document.RootElement;
            if (!root.TryGetProperty("status", out var status) || status.GetInt32() != 200)
                throw NoResult(EngineHttp.GetString(root, "error"));

            if (!root.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("translations", out var translations) ||
                translations.ValueKind != JsonValueKind.Array ||
                translations.GetArrayLength() == 0)
            {
                throw NoResult();
            }

            detected ??= EngineHttp.GetString(content, "source_language");

            foreach (var item in translations.EnumerateArray())
                builder.Append(EngineHttp.GetString(item, "translation"));
        }

        return new EngineResult(RequireText(builder.ToString(), this), detected);
    }
}

/// <summary>Lingvanex 官方网页翻译（匿名 Bearer），无需 API Key。</summary>
internal sealed class LingvanexEngine : TranslateEngine
{
    private const string HomeUrl = "https://lingvanex.com/en/translate/";
    private const string Endpoint = "https://api-b2b.backenster.com/b1/api/v3/translate";
    private const int MaxChunkCodepoints = 1000;

    private static readonly Regex TokenPattern = new(
        "API_BEARER_TOKEN\\s*=\\s*\"(Bearer [a-zA-Z0-9_\\-]{1,256})\"",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "zh-Hans_CN",
        ["zh-TW"] = "zh-Hant_TW",
        ["en"] = "en_GB",
        ["ja"] = "ja_JP",
        ["ko"] = "ko_KR",
        ["fr"] = "fr_FR",
        ["de"] = "de_DE",
        ["es"] = "es_ES",
        ["ru"] = "ru_RU",
        ["pt"] = "pt_PT",
        ["it"] = "it_IT",
        ["ar"] = "ar_SA",
        ["vi"] = "vi_VN",
        ["th"] = "th_TH",
        ["id"] = "id_ID",
        ["ms"] = "ms_MY",
        ["tr"] = "tr_TR",
        ["hi"] = "hi_IN",
        ["nl"] = "nl_NL",
        ["pl"] = "pl_PL",
        ["uk"] = "uk_UA",
        ["fa"] = "fa_IR"
    };

    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public override TranslatorKind Kind => TranslatorKind.Lingvanex;
    public override string Name => "Lingvanex";
    public override string Group => EngineGroups.Free;

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var target = LanguageCodes.AtLeast(targetLanguage, Languages, this);
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
        string target,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(context, cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = EngineHttp.JsonContent(new
            {
                to = target,
                text = chunk,
                platform = "dp"
            })
        };
        request.Headers.TryAddWithoutValidation("Authorization", token);

        using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        if (root.TryGetProperty("err", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var message = EngineHttp.GetString(error, "message");
            if (!string.IsNullOrWhiteSpace(message))
            {
                // 令牌过期时下次调用重新抓取。
                _token = null;
                throw NoResult(message);
            }
        }

        return RequireText(EngineHttp.GetString(root, "result"), this);
    }

    private async Task<string> GetTokenAsync(EngineContext context, CancellationToken cancellationToken)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _token;

        using var request = new HttpRequestMessage(HttpMethod.Get, HomeUrl);
        var html = await EngineHttp.SendTextAsync(context.Http, request, this, cancellationToken)
            .ConfigureAwait(false);

        var match = TokenPattern.Match(html);
        if (!match.Success)
            throw NoResult("未能从官网取得匿名令牌。");

        _token = match.Groups[1].Value;
        _tokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20);
        return _token;
    }
}

/// <summary>MyMemory 官方匿名接口，无需 API Key（有每日额度）。</summary>
internal sealed class MyMemoryEngine : TranslateEngine
{
    private const string Endpoint = "https://api.mymemory.translated.net/get";
    private const int MaxChunkBytes = 480;

    public override TranslatorKind Kind => TranslatorKind.MyMemory;
    public override string Name => "MyMemory";
    public override string Group => EngineGroups.Free;
    public override bool SupportsSourceLanguage => true;
    public override string? ConfigHint => "匿名每天约 5000 字符，超出后会返回额度提示。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var target = LanguageCodes.Normalize(targetLanguage);
        var source = LanguageCodes.Normalize(LanguageDetector.Detect(text) ?? "en");
        if (source == target)
            return new EngineResult(text, source);

        var chunks = TextChunker.SplitByUtf8Bytes(text, MaxChunkBytes);
        var builder = new StringBuilder();

        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                builder.Append(chunk);
                continue;
            }

            var url = $"{Endpoint}?q={Uri.EscapeDataString(chunk)}" +
                      $"&langpair={Uri.EscapeDataString(source)}%7C{Uri.EscapeDataString(target)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
                .ConfigureAwait(false);

            var root = document.RootElement;
            if (root.TryGetProperty("responseStatus", out var status) &&
                status.ValueKind == JsonValueKind.Number &&
                status.GetInt32() != 200)
            {
                throw NoResult(EngineHttp.GetString(root, "responseDetails"));
            }

            if (!root.TryGetProperty("responseData", out var data))
                throw NoResult();

            var translated = EngineHttp.GetString(data, "translatedText");
            if (string.IsNullOrWhiteSpace(translated))
                throw NoResult();

            if (translated.Contains("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{Name} 今天的免费额度已用完，请稍后再试。");

            builder.Append(translated);
        }

        return new EngineResult(RequireText(builder.ToString(), this), source);
    }
}

/// <summary>Apertium 开源机器翻译，无需 API Key（不含中日韩）。</summary>
internal sealed class ApertiumEngine : TranslateEngine
{
    private const string Endpoint = "https://apertium.org/apy/translate";
    private const int MaxChunkCodepoints = 1000;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "eng",
        ["es"] = "spa",
        ["fr"] = "fra",
        ["de"] = "deu",
        ["it"] = "ita",
        ["pt"] = "por",
        ["ca"] = "cat",
        ["gl"] = "glg",
        ["ro"] = "ron",
        ["eo"] = "epo",
        ["oc"] = "oci",
        ["cy"] = "cym",
        ["no"] = "nob",
        ["sv"] = "swe",
        ["da"] = "dan",
        ["is"] = "isl",
        ["uk"] = "ukr",
        ["ru"] = "rus",
        ["pl"] = "pol",
        ["cs"] = "ces",
        ["sk"] = "slk",
        ["sl"] = "slv",
        ["hr"] = "hrv",
        ["sr"] = "srp"
    };

    public override TranslatorKind Kind => TranslatorKind.Apertium;
    public override string Name => "Apertium";
    public override string Group => EngineGroups.Free;
    public override bool SupportsSourceLanguage => true;
    public override string? ConfigHint => "开源机器翻译，只提供部分欧洲语言互译。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var normalizedTarget = LanguageCodes.Normalize(targetLanguage);
        if (!Languages.ContainsKey(normalizedTarget) && !Languages.ContainsKey(LanguageCodes.Base(normalizedTarget)))
        {
            throw new InvalidOperationException(
                $"{Name} 没有 {LanguageCodes.ChineseName(normalizedTarget)} 语言对，" +
                "它只提供部分欧洲语言之间的互译。");
        }

        var target = LanguageCodes.AtLeast(normalizedTarget, Languages, this);
        var source = LanguageCodes.Normalize(LanguageDetector.Detect(text) ?? string.Empty);
        if (source is "" or "auto")
            throw new InvalidOperationException($"{Name} 无法识别源语言，请改用其它引擎。");
        if (source == LanguageCodes.Normalize(targetLanguage))
            return new EngineResult(text, source);

        var from = LanguageCodes.AtLeast(source, Languages, this);
        var chunks = TextChunker.Split(text, MaxChunkCodepoints);
        var builder = new StringBuilder();

        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                builder.Append(chunk);
                continue;
            }

            var url = $"{Endpoint}?langpair={from}%7C{target}" +
                      $"&q={Uri.EscapeDataString(chunk)}&format=txt";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await context.Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"{Name} 不支持 {LanguageCodes.ChineseName(source)} → " +
                    $"{LanguageCodes.ChineseName(targetLanguage)} 方向。");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("responseStatus", out var status) &&
                status.ValueKind == JsonValueKind.Number &&
                status.GetInt32() != 200)
            {
                throw new InvalidOperationException(
                    $"{Name} 不支持 {LanguageCodes.ChineseName(source)} → " +
                    $"{LanguageCodes.ChineseName(targetLanguage)} 方向。");
            }

            if (!root.TryGetProperty("responseData", out var data))
                throw NoResult();

            var translated = EngineHttp.GetString(data, "translatedText");
            if (string.IsNullOrWhiteSpace(translated))
                throw NoResult();

            builder.Append(translated);
        }

        return new EngineResult(RequireText(builder.ToString(), this), source);
    }
}

/// <summary>DeepL 网页接口（jsonrpc），无需 API Key。</summary>
internal sealed class DeepLEngine : TranslateEngine
{
    private const string Endpoint = "https://www2.deepl.com/jsonrpc?client=chrome-extension,1.0.0";
    private const int MaxChunkCodepoints = 1500;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "ZH",
        ["zh-TW"] = "ZH",
        ["en"] = "EN",
        ["ja"] = "JA",
        ["ko"] = "KO",
        ["fr"] = "FR",
        ["de"] = "DE",
        ["es"] = "ES",
        ["ru"] = "RU",
        ["pt"] = "PT",
        ["it"] = "IT",
        ["ar"] = "AR",
        ["vi"] = "VI",
        ["th"] = "TH",
        ["id"] = "ID",
        ["ms"] = "MS",
        ["tr"] = "TR",
        ["hi"] = "HI",
        ["nl"] = "NL",
        ["pl"] = "PL",
        ["uk"] = "UK",
        ["fa"] = "FA"
    };

    public override TranslatorKind Kind => TranslatorKind.DeepL;
    public override string Name => "DeepL";
    public override string Group => EngineGroups.Free;
    public override string? ConfigHint => "网页接口偶尔会限流（HTTP 429）。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var normalized = LanguageCodes.Normalize(targetLanguage);
        var target = LanguageCodes.AtLeast(normalized, Languages, this);
        var chunks = TextChunker.Split(text, MaxChunkCodepoints);
        var builder = new StringBuilder();

        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                builder.Append(chunk);
                continue;
            }

            builder.Append(await TranslateChunkAsync(chunk, target, normalized, context, cancellationToken)
                .ConfigureAwait(false));
        }

        return new EngineResult(RequireText(builder.ToString(), this));
    }

    private async Task<string> TranslateChunkAsync(
        string chunk,
        string target,
        string normalizedTarget,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var language = normalizedTarget == "zh-TW"
            ? new { target_lang = target, regionalVariant = "zh-Hant" }
            : new { target_lang = target, regionalVariant = "zh-Hans" };

        var payload = new
        {
            jsonrpc = "2.0",
            method = "LMT_handle_texts",
            @params = new
            {
                texts = new[] { new { text = chunk, requestAlternatives = 0 } },
                splitting = "newlines",
                lang = language,
                timestamp,
                commonJobParams = new { wasSpoken = false, transcribeAS = string.Empty }
            },
            id = timestamp * 1000
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = EngineHttp.JsonContent(payload)
        };
        request.Headers.Referrer = new Uri("https://www.deepl.com/");

        using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var message = EngineHttp.GetString(error, "message") ?? "未知错误";
            throw new InvalidOperationException($"{Name} 返回错误：{message}。");
        }

        if (!root.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("texts", out var texts) ||
            texts.ValueKind != JsonValueKind.Array ||
            texts.GetArrayLength() == 0)
        {
            throw NoResult();
        }

        return RequireText(EngineHttp.GetString(texts[0], "text"), this);
    }
}

/// <summary>Yandex 翻译 Android 客户端接口，无需 API Key。</summary>
internal sealed class YandexEngine : TranslateEngine
{
    private const string Endpoint = "https://translate.yandex.net/api/v1/tr.json/translate";
    private const int MaxChunkCodepoints = 1000;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "zh",
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

    public override TranslatorKind Kind => TranslatorKind.Yandex;
    public override string Name => "Yandex";
    public override string Group => EngineGroups.Free;
    public override string? ConfigHint => "Yandex 不区分繁体中文；部分网络环境会返回 403。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        if (LanguageCodes.IsTraditionalChinese(targetLanguage))
            throw new InvalidOperationException($"{Name} 不区分繁体中文，请改用其它引擎。");

        var target = LanguageCodes.AtLeast(targetLanguage, Languages, this);
        var chunks = TextChunker.Split(text, MaxChunkCodepoints);
        var builder = new StringBuilder();
        string? detected = null;

        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                builder.Append(chunk);
                continue;
            }

            var id = $"{Guid.NewGuid():N}-0-0";
            var url = $"{Endpoint}?id={id}&srv=android&target_lang={Uri.EscapeDataString(target)}" +
                      $"&text={Uri.EscapeDataString(chunk)}";

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/x-www-form-urlencoded")
            };

            using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
                .ConfigureAwait(false);

            var root = document.RootElement;
            if (root.TryGetProperty("code", out var code) &&
                code.ValueKind == JsonValueKind.Number && code.GetInt32() != 200)
            {
                throw NoResult($"状态码 {code.GetInt32()}。");
            }

            if (!root.TryGetProperty("text", out var textElement) ||
                textElement.ValueKind != JsonValueKind.Array ||
                textElement.GetArrayLength() == 0)
            {
                throw NoResult();
            }

            var translated = textElement[0].GetString();
            if (string.IsNullOrWhiteSpace(translated))
                throw NoResult();

            detected ??= EngineHttp.GetString(root, "lang");
            builder.Append(translated);
        }

        return new EngineResult(RequireText(builder.ToString(), this), detected);
    }
}

/// <summary>Reverso 官方网页翻译，无需 API Key（可能被 Cloudflare 拦截）。</summary>
internal sealed class ReversoEngine : TranslateEngine
{
    private const string Endpoint = "https://api.reverso.net/translate/v1/translation";
    private const int MaxChunkCodepoints = 1000;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "chi",
        ["zh-TW"] = "chi",
        ["en"] = "eng",
        ["ja"] = "jpn",
        ["ko"] = "kor",
        ["fr"] = "fra",
        ["de"] = "deu",
        ["es"] = "spa",
        ["ru"] = "rus",
        ["pt"] = "por",
        ["it"] = "ita",
        ["ar"] = "ara",
        ["nl"] = "nld",
        ["pl"] = "pol",
        ["tr"] = "tur"
    };

    public override TranslatorKind Kind => TranslatorKind.Reverso;
    public override string Name => "Reverso";
    public override string Group => EngineGroups.Free;
    public override string? ConfigHint => "可能被 Cloudflare 访问验证拦截，适合作为备用引擎。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var target = LanguageCodes.AtLeast(targetLanguage, Languages, this);
        var chunks = TextChunker.Split(text, MaxChunkCodepoints);
        var builder = new StringBuilder();

        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                builder.Append(chunk);
                continue;
            }

            var payload = new
            {
                format = "text",
                from = "auto",
                to = target,
                input = chunk,
                options = new
                {
                    languageDetection = true,
                    contextResults = false,
                    origin = "translation.web"
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = EngineHttp.JsonContent(payload)
            };
            request.Headers.TryAddWithoutValidation("x-reverso-origin", "translation.web");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("translation", out var translation) ||
                translation.ValueKind != JsonValueKind.Array)
            {
                throw NoResult();
            }

            var part = new StringBuilder();
            foreach (var item in translation.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    part.Append(item.GetString());
            }

            if (part.Length == 0)
                throw NoResult();

            builder.Append(part);
        }

        return new EngineResult(RequireText(builder.ToString(), this));
    }
}