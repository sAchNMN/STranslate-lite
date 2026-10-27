using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace STranslate.Lite.Engines;

/// <summary>腾讯交互翻译（transmart），无需 API Key。</summary>
internal sealed class TransmartEngine : TranslateEngine
{
    private const string Endpoint = "https://transmart.qq.com/api/imt";
    private const int MaxChunkCodepoints = 1000;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "zh",
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

    public override TranslatorKind Kind => TranslatorKind.Transmart;
    public override string Name => "腾讯交互翻译";
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

            var (translated, source) = await TranslateChunkAsync(chunk, target, context, cancellationToken)
                .ConfigureAwait(false);
            detected ??= source;
            builder.Append(translated);
        }

        return new EngineResult(RequireText(builder.ToString(), this), detected);
    }

    private async Task<(string Text, string? Source)> TranslateChunkAsync(
        string chunk,
        string target,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            header = new
            {
                fn = "auto_translation",
                client_key = $"browser-chrome-110.0.0-Mac OS-{Guid.NewGuid()}-" +
                             $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
            },
            type = "plain",
            model_category = "normal",
            source = new { text_list = new[] { chunk }, lang = "auto" },
            target = new { lang = target }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = EngineHttp.JsonContent(payload)
        };
        request.Headers.Referrer = new Uri("https://yi.qq.com/zh-CN/index");

        using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        if (!root.TryGetProperty("header", out var header) ||
            EngineHttp.GetString(header, "ret_code") != "succ")
        {
            throw NoResult("接口返回状态异常。");
        }

        var translated = ReadTranslation(root);
        var source = EngineHttp.GetString(root, "src_lang");
        return (translated, source);
    }

    private static string ReadTranslation(JsonElement root)
    {
        if (!root.TryGetProperty("auto_translation", out var translation))
            throw new InvalidOperationException("腾讯交互翻译返回结果为空。");

        if (translation.ValueKind == JsonValueKind.String)
            return translation.GetString() ?? string.Empty;

        if (translation.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();
            foreach (var item in translation.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    builder.Append(item.GetString());
            }

            return builder.ToString();
        }

        return string.Empty;
    }
}

/// <summary>火山翻译网页接口，无需 API Key。</summary>
internal sealed class VolcengineEngine : TranslateEngine
{
    private const string Endpoint = "https://translate.volcengine.com/crx/translate/v1/";
    private const int MaxChunkCodepoints = 1000;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "zh",
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

    public override TranslatorKind Kind => TranslatorKind.Volcengine;
    public override string Name => "火山翻译";
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
                source_language = "auto",
                target_language = target,
                text = chunk
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = EngineHttp.JsonContent(payload)
            };

            using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
                .ConfigureAwait(false);

            var root = document.RootElement;
            if (root.TryGetProperty("base_resp", out var baseResp) &&
                baseResp.TryGetProperty("status_code", out var statusCode) &&
                statusCode.ValueKind == JsonValueKind.Number &&
                statusCode.GetInt32() != 0)
            {
                throw NoResult(EngineHttp.GetString(baseResp, "status_message"));
            }

            var translated = EngineHttp.GetString(root, "translation");
            if (string.IsNullOrWhiteSpace(translated))
                throw NoResult();

            detected ??= EngineHttp.GetString(root, "detected_language");
            builder.Append(translated);
        }

        return new EngineResult(RequireText(builder.ToString(), this), detected);
    }
}

/// <summary>阿里翻译网页接口（临时 CSRF + multipart），无需 API Key。</summary>
internal sealed class AlibabaEngine : TranslateEngine
{
    private const string BaseUrl = "https://translate.alibaba.com/api/translate";
    private const int MaxChunkCodepoints = 1000;

    private string? _csrfToken;
    private string? _csrfHeader;
    private DateTimeOffset _csrfExpiresAt = DateTimeOffset.MinValue;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "zh",
        ["zh-TW"] = "zh-tw",
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

    public override TranslatorKind Kind => TranslatorKind.Alibaba;
    public override string Name => "阿里翻译";
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

            var (token, headerName) = await GetTokenAsync(context, cancellationToken).ConfigureAwait(false);

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(chunk, Encoding.UTF8), "query");
            content.Add(new StringContent("auto", Encoding.UTF8), "srcLang");
            content.Add(new StringContent(target, Encoding.UTF8), "tgtLang");
            content.Add(new StringContent("general", Encoding.UTF8), "domain");
            content.Add(new StringContent(token, Encoding.UTF8), "_csrf");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/text")
            {
                Content = content
            };
            request.Headers.TryAddWithoutValidation(headerName, token);

            using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
                .ConfigureAwait(false);

            var root = document.RootElement;
            var success = root.TryGetProperty("success", out var successElement) &&
                          successElement.ValueKind == JsonValueKind.True;
            if (!success)
                throw NoResult(EngineHttp.GetString(root, "message"));

            if (!root.TryGetProperty("data", out var data))
                throw NoResult();

            var translated = EngineHttp.GetString(data, "translateText");
            detected ??= EngineHttp.GetString(data, "detectLanguage");
            if (string.IsNullOrWhiteSpace(translated))
                throw NoResult();

            builder.Append(translated);
        }

        return new EngineResult(RequireText(builder.ToString(), this), detected);
    }

    private async Task<(string Token, string Header)> GetTokenAsync(
        EngineContext context,
        CancellationToken cancellationToken)
    {
        if (_csrfToken is not null &&
            _csrfHeader is not null &&
            DateTimeOffset.UtcNow < _csrfExpiresAt)
        {
            return (_csrfToken, _csrfHeader);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/csrftoken");
        using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
            .ConfigureAwait(false);

        var token = EngineHttp.GetString(document.RootElement, "token");
        var header = EngineHttp.GetString(document.RootElement, "headerName");
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(header))
            throw NoResult("未能获取网页令牌。");

        _csrfToken = token;
        _csrfHeader = header;
        _csrfExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        return (token, header);
    }
}

/// <summary>有道网页翻译（jsonapi_s + 网页签名），无需 API Key。</summary>
internal sealed class YoudaoEngine : TranslateEngine
{
    private const string Endpoint = "https://dict.youdao.com/jsonapi_s";
    private const string KeyFrom = "webfanyi.webmain";
    private const string Client = "webmain";
    private const string Secret = "t2he2k4m2g6QKRigK0KAmSpXKgAezyWg";
    private const int MaxChunkCodepoints = 500;

    public override TranslatorKind Kind => TranslatorKind.Youdao;
    public override string Name => "有道翻译";
    public override string Group => EngineGroups.Free;
    public override string? ConfigHint => "使用有道网页翻译接口，目前仅支持中文与英文互译。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var (from, to) = ResolveDirection(text, targetLanguage);
        if (from == to)
            return new EngineResult(text, from);

        var chunks = TextChunker.Split(text, MaxChunkCodepoints);
        var builder = new StringBuilder();

        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                builder.Append(chunk);
                continue;
            }

            builder.Append(await TranslateChunkAsync(chunk, from, to, context, cancellationToken)
                .ConfigureAwait(false));
        }

        return new EngineResult(RequireText(builder.ToString(), this));
    }

    private (string From, string To) ResolveDirection(string text, string targetLanguage)
    {
        var target = LanguageCodes.Normalize(targetLanguage);
        var to = target switch
        {
            "zh-CN" => "zh-CHS",
            "zh-TW" => "zh-CHT",
            "en" => "en",
            _ => throw new InvalidOperationException(
                $"{Name} 目前仅支持中文与英文，无法翻译为 {LanguageCodes.ChineseName(target)}。")
        };

        // 有道不做可靠的自动识别，用本地语种识别补齐源语言，同时便于校验返回方向。
        var sourceIsChinese = LanguageCodes.IsChinese(LanguageDetector.Detect(text) ?? "en");
        var from = sourceIsChinese ? "zh-CHS" : "en";
        return (from, to);
    }

    private async Task<string> TranslateChunkAsync(
        string chunk,
        string from,
        string to,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var timestamp = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
                         ((chunk + KeyFrom).Length % 10)).ToString(CultureInfo.InvariantCulture);
        var queryDigest = Md5Hex(chunk + KeyFrom);
        var sign = Md5Hex($"{Client}{chunk}{timestamp}{Secret}{queryDigest}");

        var url = $"{Endpoint}?doctype=json&jsonversion=4" +
                  $"&q={Uri.EscapeDataString(chunk)}" +
                  $"&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}" +
                  $"&t={timestamp}&client={Client}&sign={sign}&keyfrom={KeyFrom}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        if (root.TryGetProperty("fanyi", out var fanyi))
        {
            var direction = EngineHttp.GetString(fanyi, "type");
            if (!string.IsNullOrWhiteSpace(direction) &&
                !direction.Replace("-", string.Empty).Equals($"{from}2{to}".Replace("-", string.Empty),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{Name} 返回了与请求不一致的翻译方向。");
            }

            var translated = EngineHttp.GetString(fanyi, "tran");
            if (!string.IsNullOrWhiteSpace(translated))
                return translated.Trim();
        }

        // 单词/短语只返回词典结构，从网页释义里取最短的对应译文。
        if (root.TryGetProperty("web_trans", out var webTrans) &&
            webTrans.TryGetProperty("web-translation", out var entries) &&
            entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("trans", out var trans) ||
                    trans.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in trans.EnumerateArray())
                {
                    var value = EngineHttp.GetString(item, "value");
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }
        }

        throw NoResult();
    }

    private static string Md5Hex(string text) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>金山词霸匿名网页翻译（AES-ECB 签名与响应加密），无需 API Key。</summary>
internal sealed class IcibaEngine : TranslateEngine
{
    private const string Endpoint = "https://ifanyi.iciba.com/index.php?c=trans&m=fyV2";
    private const string Client = "6";
    private const string AuthUser = "key_web_new_fanyi";
    private const string SignPrefix = "6key_web_new_fanyi6dVjYLFyzfkFkk";
    private const string SignKey = "L4fBtD5fLC9FQw22";
    private const string ResponseKey = "aahc3TfyfCEmER33";
    private const int MaxChunkCodepoints = 1000;

    public override TranslatorKind Kind => TranslatorKind.Iciba;
    public override string Name => "金山词霸";
    public override string Group => EngineGroups.Free;
    public override string? ConfigHint => "使用金山词霸匿名网页接口，目前仅支持中文与英文互译。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var target = LanguageCodes.Normalize(targetLanguage);
        var to = target switch
        {
            "zh-CN" => "zh",
            "en" => "en",
            _ => throw new InvalidOperationException(
                $"{Name} 目前仅支持中文与英文，无法翻译为 {LanguageCodes.ChineseName(target)}。")
        };

        // 词霸不接受 auto 源语言，用本地语种识别补齐。
        var from = LanguageCodes.IsChinese(LanguageDetector.Detect(text) ?? "en") ? "zh" : "en";
        if (from == to)
            return new EngineResult(text, from);

        var chunks = TextChunker.Split(text, MaxChunkCodepoints);
        var builder = new StringBuilder();

        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                builder.Append(chunk);
                continue;
            }

            builder.Append(await TranslateChunkAsync(chunk, from, to, context, cancellationToken)
                .ConfigureAwait(false));
        }

        return new EngineResult(RequireText(builder.ToString(), this), from);
    }

    private async Task<string> TranslateChunkAsync(
        string chunk,
        string from,
        string target,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var digest = Md5Hex(SignPrefix + chunk)[..16];
        var sign = Convert.ToBase64String(Encrypt(digest, SignKey));
        var body = new Dictionary<string, string>
        {
            ["from"] = from,
            ["to"] = target,
            ["q"] = chunk,
            ["client"] = Client,
            ["auth_user"] = AuthUser,
            ["sign"] = sign
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new FormUrlEncodedContent(body)
        };
        request.Headers.TryAddWithoutValidation("Origin", "https://www.iciba.com");
        request.Headers.Referrer = new Uri("https://www.iciba.com/");

        using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        if (!root.TryGetProperty("status", out var status) || !TryReadInt(status, out var statusCode) || statusCode != 1)
            throw NoResult("接口返回状态异常。");

        var content = EngineHttp.GetString(root, "content");
        if (string.IsNullOrWhiteSpace(content))
            throw NoResult();

        var decrypted = Decrypt(content, ResponseKey);
        using var payload = JsonDocument.Parse(decrypted);
        var result = payload.RootElement;

        if (result.TryGetProperty("err_no", out var errorNumber) &&
            TryReadInt(errorNumber, out var errorCode) && errorCode != 0)
        {
            throw NoResult($"错误码 {errorCode}。");
        }

        return RequireText(EngineHttp.GetString(result, "out"), this);
    }

    private static bool TryReadInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(element.GetString(), out value),
            _ => false
        };
    }

    private static string Md5Hex(string text) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static byte[] Encrypt(string plain, string key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = Encoding.UTF8.GetBytes(key);
        using var encryptor = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plain);
        return encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
    }

    private static string Decrypt(string base64, string key)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = Encoding.UTF8.GetBytes(key);
        using var decryptor = aes.CreateDecryptor();
        var bytes = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(bytes, 0, bytes.Length));
    }
}