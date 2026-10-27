namespace STranslate.Lite.Engines;

/// <summary>
/// 引擎注册表。顺序即设置界面中的展示顺序：先免密钥直连，再本地 / 自建，最后大模型接口。
/// </summary>
internal static class EngineRegistry
{
    private static readonly TranslateEngine[] AllEngines =
    [
        new GoogleEngine(),
        new MicrosoftEngine(),
        new BilibiliEngine(),
        new TransmartEngine(),
        new VolcengineEngine(),
        new AlibabaEngine(),
        new YoudaoEngine(),
        new IcibaEngine(),
        new LaraEngine(),
        new LingvanexEngine(),
        new MyMemoryEngine(),
        new ApertiumEngine(),
        new DeepLEngine(),
        new YandexEngine(),
        new ReversoEngine(),
        new OllamaEngine(),
        new LibreTranslateEngine(),
        new DeepLxEngine(),
        new MTranServerEngine(),
        new OpenAiCompatibleEngine()
    ];

    private static readonly Dictionary<TranslatorKind, TranslateEngine> Lookup =
        AllEngines.ToDictionary(engine => engine.Kind);

    public static IReadOnlyList<TranslateEngine> All => AllEngines;

    public static TranslateEngine Get(TranslatorKind kind) =>
        Lookup.TryGetValue(kind, out var engine) ? engine : Lookup[TranslatorKind.Google];
}