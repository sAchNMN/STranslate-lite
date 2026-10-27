using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace STranslate.Lite;

internal enum TriggerMode
{
    XButton1,
    XButton2,
    Hotkey,
    Disabled
}

/// <summary>
/// 翻译引擎标识。配置文件使用字符串枚举，新增引擎只能追加，不能重命名已有值。
/// </summary>
internal enum TranslatorKind
{
    Google = 0,
    OpenAiCompatible = 1,
    Microsoft = 2,
    Bilibili = 3,
    Transmart = 4,
    Volcengine = 5,
    Alibaba = 6,
    Youdao = 7,
    Iciba = 8,
    Lara = 9,
    Lingvanex = 10,
    MyMemory = 11,
    Apertium = 12,
    DeepL = 13,
    Yandex = 14,
    Reverso = 15,
    LibreTranslate = 16,
    Ollama = 17,
    DeepLx = 18,
    MTranServer = 19
}

/// <summary>
/// 程序持久化配置。配置文件位于 %APPDATA%\STranslate.Lite\settings.json。
/// </summary>
internal sealed class AppSettings
{
    public int Version { get; set; } = 2;

    public TriggerMode TriggerMode { get; set; } = TriggerMode.XButton1;

    public HotkeyDefinition Hotkey { get; set; } = HotkeyDefinition.CreateDefault();

    public bool IgnoreFullscreen { get; set; } = true;

    /// <summary>目标语言；auto 表示中文翻译成英文，其它语言翻译成简体中文。</summary>
    public string TargetLanguage { get; set; } = "auto";

    public TranslatorKind Translator { get; set; } = TranslatorKind.Google;

    /// <summary>按引擎分别保存的地址 / 密钥 / 模型，切换引擎时互不覆盖。</summary>
    public Dictionary<string, ServiceOptions> Services { get; set; } = new();

    /// <summary>旧版本（v1）的 OpenAI 配置，读取后迁移进 Services，不再写回。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ServiceOptions? OpenAi { get; set; }

    public bool AutoStart { get; set; }

    public AppSettings Clone()
    {
        var clone = new AppSettings
        {
            Version = Version,
            TriggerMode = TriggerMode,
            Hotkey = Hotkey.Clone(),
            IgnoreFullscreen = IgnoreFullscreen,
            TargetLanguage = TargetLanguage,
            Translator = Translator,
            AutoStart = AutoStart
        };

        foreach (var (key, value) in Services)
            clone.Services[key] = value.Clone();

        return clone;
    }

    public ServiceOptions GetService(TranslatorKind kind) =>
        Services.TryGetValue(kind.ToString(), out var service) ? service : new ServiceOptions();

    public void SetService(TranslatorKind kind, ServiceOptions service) =>
        Services[kind.ToString()] = service;

    public void Normalize()
    {
        if (!Enum.IsDefined(TriggerMode))
            TriggerMode = TriggerMode.XButton1;

        Hotkey ??= HotkeyDefinition.CreateDefault();
        if (!Hotkey.IsValid)
            Hotkey = HotkeyDefinition.CreateDefault();

        if (string.IsNullOrWhiteSpace(TargetLanguage))
            TargetLanguage = "auto";
        else
            TargetLanguage = TargetLanguage.Trim();

        if (!Enum.IsDefined(Translator))
            Translator = TranslatorKind.Google;

        Services ??= new Dictionary<string, ServiceOptions>();

        // v1 配置迁移：OpenAi -> Services[OpenAiCompatible]。
        if (OpenAi is not null)
        {
            if (!Services.ContainsKey(nameof(TranslatorKind.OpenAiCompatible)))
                Services[nameof(TranslatorKind.OpenAiCompatible)] = OpenAi;

            OpenAi = null;
        }

        foreach (var service in Services.Values)
            service.Normalize();

        if (Version < 2)
            Version = 2;
    }
}

/// <summary>
/// 单个引擎的服务参数。并非每个引擎都会用到所有字段。
/// </summary>
internal sealed class ServiceOptions
{
    public string Endpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public ServiceOptions Clone() => new()
    {
        Endpoint = Endpoint,
        ApiKey = ApiKey,
        Model = Model
    };

    public void Normalize()
    {
        Endpoint = Endpoint?.Trim() ?? string.Empty;
        ApiKey = ApiKey?.Trim() ?? string.Empty;
        Model = Model?.Trim() ?? string.Empty;
    }
}

/// <summary>
/// 全局快捷键定义。VirtualKey 使用 Win32 Virtual-Key Code。
/// </summary>
internal sealed class HotkeyDefinition
{
    public bool Ctrl { get; set; }

    public bool Alt { get; set; }

    public bool Shift { get; set; }

    public bool Win { get; set; }

    public int VirtualKey { get; set; }

    [JsonIgnore]
    public bool IsValid => VirtualKey > 0 && (Ctrl || Alt || Shift || Win);

    [JsonIgnore]
    public string Display => ToDisplayString();

    public static HotkeyDefinition CreateDefault() => new()
    {
        Ctrl = true,
        Alt = true,
        VirtualKey = (int)Keys.T
    };

    public HotkeyDefinition Clone() => new()
    {
        Ctrl = Ctrl,
        Alt = Alt,
        Shift = Shift,
        Win = Win,
        VirtualKey = VirtualKey
    };

    public string ToDisplayString()
    {
        if (VirtualKey <= 0)
            return "未设置";

        var parts = new List<string>(5);
        if (Ctrl)
            parts.Add("Ctrl");
        if (Alt)
            parts.Add("Alt");
        if (Shift)
            parts.Add("Shift");
        if (Win)
            parts.Add("Win");
        parts.Add(GetKeyDisplayName(VirtualKey));
        return string.Join(" + ", parts);
    }

    public static string GetKeyDisplayName(int virtualKey)
    {
        return (Keys)virtualKey switch
        {
            Keys.Oemtilde => "`",
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.OemOpenBrackets => "[",
            Keys.Oem6 => "]",
            Keys.Oem5 => "\\",
            Keys.Oem1 => ";",
            Keys.Oem7 => "'",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.Oem102 => "\\",
            Keys.Space => "Space",
            Keys.Escape => "Esc",
            Keys.Back => "Backspace",
            Keys.Return => "Enter",
            Keys.PageUp => "PageUp",
            Keys.PageDown => "PageDown",
            Keys.Capital => "CapsLock",
            Keys.Scroll => "ScrollLock",
            Keys.PrintScreen => "PrintScreen",
            Keys.Left => "←",
            Keys.Right => "→",
            Keys.Up => "↑",
            Keys.Down => "↓",
            _ => ((Keys)virtualKey).ToString()
        };
    }
}