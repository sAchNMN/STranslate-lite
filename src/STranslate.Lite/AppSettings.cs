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

internal enum TranslatorKind
{
    Google,
    OpenAiCompatible
}

/// <summary>
/// 程序持久化配置。配置文件位于 %APPDATA%\STranslate.Lite\settings.json。
/// </summary>
internal sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public TriggerMode TriggerMode { get; set; } = TriggerMode.XButton1;

    public HotkeyDefinition Hotkey { get; set; } = HotkeyDefinition.CreateDefault();

    public bool IgnoreFullscreen { get; set; } = true;

    /// <summary>目标语言；auto 表示中文翻译成英文，其它语言翻译成简体中文。</summary>
    public string TargetLanguage { get; set; } = "auto";

    public TranslatorKind Translator { get; set; } = TranslatorKind.Google;

    public OpenAiCompatibleOptions OpenAi { get; set; } = new();

    public bool AutoStart { get; set; }

    public AppSettings Clone() => new()
    {
        Version = Version,
        TriggerMode = TriggerMode,
        Hotkey = Hotkey.Clone(),
        IgnoreFullscreen = IgnoreFullscreen,
        TargetLanguage = TargetLanguage,
        Translator = Translator,
        OpenAi = OpenAi.Clone(),
        AutoStart = AutoStart
    };

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

        OpenAi ??= new OpenAiCompatibleOptions();
        OpenAi.Normalize();
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

internal sealed class OpenAiCompatibleOptions
{
    public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-4o-mini";

    public OpenAiCompatibleOptions Clone() => new()
    {
        Endpoint = Endpoint,
        ApiKey = ApiKey,
        Model = Model
    };

    public void Normalize()
    {
        Endpoint = string.IsNullOrWhiteSpace(Endpoint)
            ? "https://api.openai.com/v1/chat/completions"
            : Endpoint.Trim();
        ApiKey = ApiKey?.Trim() ?? string.Empty;
        Model = string.IsNullOrWhiteSpace(Model) ? "gpt-4o-mini" : Model.Trim();
    }
}