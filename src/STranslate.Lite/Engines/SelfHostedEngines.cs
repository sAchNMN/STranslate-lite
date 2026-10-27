using System.Net.Http;
using System.Text;

namespace STranslate.Lite.Engines;

/// <summary>自建服务地址拼装：允许只填基地址。</summary>
internal static class EndpointBuilder
{
    public static string WithPath(string endpoint, string path)
    {
        var value = endpoint.Trim();
        if (value.EndsWith(path, StringComparison.OrdinalIgnoreCase))
            return value;

        return value.TrimEnd('/') + path;
    }

    public static Uri RequireHttp(string endpoint, TranslateEngine engine)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{engine.Name} 需要填写有效的 HTTP/HTTPS 服务地址。");
        }

        return uri;
    }
}

/// <summary>LibreTranslate（自建实例，免费开源）。</summary>
internal sealed class LibreTranslateEngine : TranslateEngine
{
    private const int MaxChunkCodepoints = 2000;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "zh",
        ["zh-TW"] = "zh",
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

    public override TranslatorKind Kind => TranslatorKind.LibreTranslate;
    public override string Name => "LibreTranslate";
    public override string Group => EngineGroups.SelfHosted;
    public override bool SupportsEndpoint => true;
    public override string? DefaultEndpoint => "http://127.0.0.1:5000";
    public override string? ConfigHint => "自建服务，只填基地址，程序自动补 /translate。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var endpoint = EndpointBuilder.WithPath(
            context.Endpoint(DefaultEndpoint),
            "/translate");
        EndpointBuilder.RequireHttp(endpoint, this);

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

            object payload = string.IsNullOrWhiteSpace(context.Service.ApiKey)
                ? new { q = chunk, source = "auto", target, format = "text" }
                : new { q = chunk, source = "auto", target, format = "text", api_key = context.Service.ApiKey };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = EngineHttp.JsonContent(payload)
            };

            using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
                .ConfigureAwait(false);

            var root = document.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                var message = error.ValueKind == System.Text.Json.JsonValueKind.String
                    ? error.GetString()
                    : "接口返回错误";
                throw new InvalidOperationException($"{Name} 返回错误：{message}。");
            }

            builder.Append(RequireText(EngineHttp.GetString(root, "translatedText"), this));
        }

        return new EngineResult(RequireText(builder.ToString(), this));
    }
}

/// <summary>Ollama 本地大模型（免费）。</summary>
internal sealed class OllamaEngine : TranslateEngine
{
    private const string DefaultModelName = "qwen2.5:7b";
    private const int MaxChunkCodepoints = 1500;

    public override TranslatorKind Kind => TranslatorKind.Ollama;
    public override string Name => "Ollama";
    public override string Group => EngineGroups.SelfHosted;
    public override bool SupportsEndpoint => true;
    public override bool SupportsModel => true;
    public override string? DefaultEndpoint => "http://127.0.0.1:11434";
    public override string? DefaultModel => DefaultModelName;
    public override string? ConfigHint => "本地大模型翻译，需要先安装并运行 Ollama。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var endpoint = EndpointBuilder.WithPath(context.Endpoint(DefaultEndpoint), "/api/chat");
        EndpointBuilder.RequireHttp(endpoint, this);

        var targetName = LanguageCodes.ChineseName(targetLanguage);
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
                model = context.Model(DefaultModelName),
                stream = false,
                options = new { temperature = 0 },
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = $"你是精确的翻译引擎。把用户输入翻译成{targetName}，" +
                                  "只输出译文，不要解释、不要引号、不要 Markdown。"
                    },
                    new { role = "user", content = chunk }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = EngineHttp.JsonContent(payload)
            };

            using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content) ||
                content.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                throw NoResult();
            }

            builder.Append(RequireText(content.GetString(), this));
        }

        return new EngineResult(RequireText(builder.ToString(), this));
    }
}

/// <summary>DeepLX（DeepL 兼容的自建 / 公益接口）。</summary>
internal sealed class DeepLxEngine : TranslateEngine
{
    private const int MaxChunkCodepoints = 1500;

    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = "ZH",
        ["zh-TW"] = "ZH-HANT",
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

    public override TranslatorKind Kind => TranslatorKind.DeepLx;
    public override string Name => "DeepLX";
    public override string Group => EngineGroups.SelfHosted;
    public override bool SupportsEndpoint => true;
    public override string? DefaultEndpoint => "http://127.0.0.1:1188";
    public override string? ConfigHint => "自建 DeepLX 服务，只填基地址即可。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var endpoint = EndpointBuilder.WithPath(context.Endpoint(DefaultEndpoint), "/translate");
        EndpointBuilder.RequireHttp(endpoint, this);

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
                text = chunk,
                source_lang = "AUTO",
                target_lang = target
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = EngineHttp.JsonContent(payload)
            };

            using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
                .ConfigureAwait(false);

            var root = document.RootElement;
            if (root.TryGetProperty("code", out var code) &&
                code.ValueKind == System.Text.Json.JsonValueKind.Number &&
                code.GetInt32() != 200)
            {
                throw NoResult(EngineHttp.GetString(root, "message"));
            }

            builder.Append(RequireText(EngineHttp.GetString(root, "data"), this));
        }

        return new EngineResult(RequireText(builder.ToString(), this));
    }
}

/// <summary>MTranServer 本地翻译服务（免费开源）。</summary>
internal sealed class MTranServerEngine : TranslateEngine
{
    private const int MaxChunkCodepoints = 1500;

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

    public override TranslatorKind Kind => TranslatorKind.MTranServer;
    public override string Name => "MTranServer";
    public override string Group => EngineGroups.SelfHosted;
    public override bool SupportsEndpoint => true;
    public override string? DefaultEndpoint => "http://127.0.0.1:8989";
    public override string? ConfigHint => "自建服务，只填基地址，程序自动补 /translate。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        var endpoint = EndpointBuilder.WithPath(context.Endpoint(DefaultEndpoint), "/translate");
        EndpointBuilder.RequireHttp(endpoint, this);

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

            var payload = new { from = "auto", to = target, text = chunk };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = EngineHttp.JsonContent(payload)
            };

            using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
                .ConfigureAwait(false);

            builder.Append(RequireText(EngineHttp.GetString(document.RootElement, "result"), this));
        }

        return new EngineResult(RequireText(builder.ToString(), this));
    }
}

/// <summary>OpenAI 兼容大模型接口（DeepSeek、通义、Moonshot、LM Studio 等）。</summary>
internal sealed class OpenAiCompatibleEngine : TranslateEngine
{
    private const string DefaultUrl = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModelName = "gpt-4o-mini";
    private const int MaxChunkCodepoints = 2000;

    public override TranslatorKind Kind => TranslatorKind.OpenAiCompatible;
    public override string Name => "OpenAI 兼容接口";
    public override string Group => EngineGroups.LargeModel;
    public override bool SupportsEndpoint => true;
    public override bool SupportsModel => true;
    public override bool RequiresApiKey => true;
    public override string? DefaultEndpoint => DefaultUrl;
    public override string? DefaultModel => DefaultModelName;
    public override string? ConfigHint =>
        "可接 DeepSeek、Moonshot 等，地址填到 /v1。";

    public override async Task<EngineResult> TranslateAsync(
        string text,
        string targetLanguage,
        EngineContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Service.ApiKey))
            throw new InvalidOperationException($"{Name} 尚未配置 API Key。");

        var endpoint = context.Endpoint(DefaultUrl);
        if (!endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
            endpoint = EndpointBuilder.WithPath(endpoint, "/chat/completions");
        EndpointBuilder.RequireHttp(endpoint, this);

        var targetName = LanguageCodes.ChineseName(targetLanguage);
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
                model = context.Model(DefaultModelName),
                temperature = 0,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = $"You are a precise translation engine. Translate the user's text into {targetName}. " +
                                  "Output only the translation, without explanations, quotes, or Markdown."
                    },
                    new { role = "user", content = chunk }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = EngineHttp.JsonContent(payload)
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {context.Service.ApiKey}");

            using var document = await EngineHttp.SendAsync(context.Http, request, this, cancellationToken)
                .ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != System.Text.Json.JsonValueKind.Array ||
                choices.GetArrayLength() == 0 ||
                !choices[0].TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content) ||
                content.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                throw NoResult();
            }

            builder.Append(RequireText(content.GetString(), this));
        }

        return new EngineResult(RequireText(builder.ToString(), this));
    }
}