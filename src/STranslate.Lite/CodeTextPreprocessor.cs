using System.Text;
using System.Text.RegularExpressions;

namespace STranslate.Lite;

/// <summary>
/// 一次代码标识符拆分的方案。<see cref="Tokens"/> 为空表示原文不需要改写。
/// </summary>
internal sealed class CodeTextPlan
{
    public static readonly CodeTextPlan None = new(string.Empty, string.Empty, []);

    public CodeTextPlan(string original, string rewritten, IReadOnlyList<string> tokens)
    {
        Original = original;
        Rewritten = rewritten;
        Tokens = tokens;
    }

    /// <summary>用户选中的原文。</summary>
    public string Original { get; }

    /// <summary>把标识符拆成普通单词后的文本，用于重试请求。</summary>
    public string Rewritten { get; }

    /// <summary>被改写的标识符，按出现顺序排列。</summary>
    public IReadOnlyList<string> Tokens { get; }

    public bool Applied => Tokens.Count > 0;

    /// <summary>整段输入就是一个标识符（如 current_thread）。</summary>
    public bool IsSingleToken =>
        Tokens.Count == 1 && string.Equals(Original.Trim(), Tokens[0], StringComparison.Ordinal);
}

/// <summary>
/// 代码标识符预处理：把 <c>current_thread</c>、<c>MAX_BUFFER_SIZE</c>、<c>getUserName</c>
/// 这类连写记号拆成普通单词后再交给翻译引擎。
/// </summary>
/// <remarks>
/// 机器翻译引擎会把下划线 / 驼峰连写的标识符当成不可拆分的代码记号原样返回，
/// 于是浮窗里看起来就是“没有翻译”。拆成自然语言单词后，引擎才能给出“当前线程”这类译文。
/// 只处理真正的标识符：URL、邮箱、文件路径和带扩展名的文件名都会跳过；
/// 没有下划线的驼峰词还要求至少三段或含全大写缩写，避免把 iPhone、JavaScript 拆坏。
/// </remarks>
internal static class CodeTextPreprocessor
{
    /// <summary>超过这个长度多半是整段文章，改写和重试都没有意义。</summary>
    private const int MaxInputLength = 2000;

    private static readonly Regex ProtectedPattern = new(
        @"(?<url>\b(?:https?|ftp|file)://[^\s""'<>]+)" +
        @"|(?<mail>\b[\w.+-]+@[\w-]+(?:\.[\w-]+)+)" +
        @"|(?<winpath>\b[A-Za-z]:\\[^\s""'<>|]+)" +
        @"|(?<unixpath>(?<![\w.])/(?:[\w.+-]+/)+[\w.+-]*)" +
        @"|(?<file>\b[\w-]+\.(?:json|jsonl|ya?ml|toml|ini|cfg|conf|txt|md|log|cs|csproj|sln|py|js|mjs|ts|tsx|jsx|java|kt|go|rs|c|h|cpp|hpp|sh|bat|ps1|cmd|html|css|scss|png|jpe?g|gif|bmp|svg|ico|zip|7z|rar|exe|dll|so|dylib|db|sqlite|sql|lock|props|targets)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 为原文生成拆分方案。不需要改写时返回 <see cref="CodeTextPlan.None"/>。
    /// </summary>
    public static CodeTextPlan CreatePlan(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxInputLength)
            return CodeTextPlan.None;

        // 快速排除：既没有下划线也没有大小写变化时不可能需要拆分。
        if (!MayContainIdentifier(text))
            return CodeTextPlan.None;

        var protectedSpans = FindProtectedSpans(text);
        var builder = new StringBuilder(text.Length + 16);
        var tokens = new List<string>();
        var index = 0;

        while (index < text.Length)
        {
            if (!IsIdentifierChar(text[index]))
            {
                builder.Append(text[index]);
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && IsIdentifierChar(text[index]))
                index++;

            var token = text[start..index];
            if (IsProtected(protectedSpans, start, index) || !TrySplit(token, out var words))
            {
                builder.Append(token);
                continue;
            }

            tokens.Add(token);
            builder.Append(string.Join(' ', words));
        }

        if (tokens.Count == 0)
            return CodeTextPlan.None;

        var rewritten = builder.ToString();

        // 整段就是一个标识符时，把原词一起发出去：引擎既能看到上下文，也不会把它当成未知专名。
        if (tokens.Count == 1 && string.Equals(text.Trim(), tokens[0], StringComparison.Ordinal))
            rewritten = $"{tokens[0]} ({rewritten.Trim()})";

        return new CodeTextPlan(text, rewritten, tokens);
    }

    /// <summary>
    /// 去掉译文里对原标识符的回显，例如 <c>current_thread（当前线程）</c> 只保留“当前线程”。
    /// </summary>
    public static string StripEcho(CodeTextPlan plan, string translated)
    {
        if (!plan.IsSingleToken)
            return translated;

        var token = plan.Tokens[0];
        var text = translated.Trim();
        if (!text.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            return translated;

        var rest = text[token.Length..].Trim();
        if (rest.Length < 2)
            return translated;

        if ((rest[0] is '（' or '(' or '[' or '【') && (rest[^1] is '）' or ')' or ']' or '】'))
            return rest[1..^1].Trim();

        if (rest[0] is '：' or ':' or '-' or '=' or '—' or '·' or '，' or ',')
        {
            var cleaned = rest.TrimStart('：', ':', '-', '=', '—', '·', '，', ',').Trim();
            return cleaned.Length > 0 ? cleaned : translated;
        }

        // 没有标点分隔，但后半段已经是译文（含汉字）时同样可以只保留译文。
        return ContainsCjk(rest) && !rest.Contains(token, StringComparison.OrdinalIgnoreCase)
            ? rest
            : translated;
    }

    /// <summary>译文里是否还残留原样未翻的标识符。</summary>
    public static bool ContainsUntouched(CodeTextPlan plan, string translated) =>
        plan.Tokens.Any(token => translated.Contains(token, StringComparison.Ordinal));

    private static bool MayContainIdentifier(string text)
    {
        if (text.Contains('_'))
            return true;

        // 下划线以外的候选：驼峰（fooBar）与缩写（HTTPClient）都从这里进入，
        // 具体要不要拆由 TrySplit 决定。
        foreach (var character in text)
        {
            if (char.IsUpper(character))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 标识符字符只取 ASCII：中文、日文等直接当作分隔符，
    /// 避免把 “使用current_thread获取” 这类中英混排整段当成一个记号。
    /// </summary>
    private static bool IsIdentifierChar(char value) =>
        value < 128 && (char.IsLetterOrDigit(value) || value is '_' or '$');

    /// <summary>译文里是否已经出现中日韩文字，用于判断引擎到底有没有翻译。</summary>
    internal static bool ContainsCjk(string text)
    {
        foreach (var character in text)
        {
            if (character is >= '\u3400' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF' or
                >= '\u3040' and <= '\u30FF' or >= '\uAC00' and <= '\uD7AF')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 拆分单个标识符。返回 false 表示它不像代码标识符，保持原样。
    /// </summary>
    private static bool TrySplit(string token, out List<string> words)
    {
        words = [];

        var trimmed = token.Trim('_');
        if (trimmed.Length < 2 || !trimmed.Any(char.IsLetter))
            return false;

        var groups = trimmed.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (groups.Length == 0)
            return false;

        var segments = new List<string>();
        foreach (var group in groups)
            segments.AddRange(SplitCamel(group));

        var hasUnderscore = groups.Length > 1 || trimmed.Length != token.Length;

        if (!hasUnderscore)
        {
            // 没有下划线：只有三段以上的驼峰，或者含全大写缩写（HTTPClient）才拆。
            if (segments.Count < 2)
                return false;
            if (segments.Count < 3 && !segments.Any(IsAcronym))
                return false;
        }

        foreach (var segment in segments)
            words.Add(segment.ToLowerInvariant());

        return words.Count > 0;
    }

    private static bool IsAcronym(string value) =>
        value.Length >= 2 && value.All(char.IsUpper);

    /// <summary>
    /// 按驼峰切分：<c>getUserName</c> → get / User / Name，<c>HTTPClient</c> → HTTP / Client。
    /// 数字与字母之间不切，避免把 utf8、sha256 拆成两段。
    /// </summary>
    private static IEnumerable<string> SplitCamel(string value)
    {
        var start = 0;
        for (var i = 1; i < value.Length; i++)
        {
            var previous = value[i - 1];
            var current = value[i];
            if (!char.IsLetter(previous) || !char.IsLetter(current))
                continue;

            var boundary = char.IsLower(previous) && char.IsUpper(current);
            if (!boundary && char.IsUpper(previous) && char.IsUpper(current) &&
                i + 1 < value.Length && char.IsLower(value[i + 1]))
            {
                // 缩写后面跟单词：HTTPClient → HTTP | Client。
                // 要求缩写至少 3 个字母，避免把 SQLite 拆成 SQ + Lite。
                var runLength = 1;
                for (var j = i - 2; j >= 0 && char.IsUpper(value[j]); j--)
                    runLength++;

                boundary = runLength >= 3;
            }

            if (!boundary)
                continue;

            yield return value[start..i];
            start = i;
        }

        if (start < value.Length)
            yield return value[start..];
    }

    private static List<(int Start, int End)> FindProtectedSpans(string text)
    {
        var spans = new List<(int Start, int End)>();
        foreach (Match match in ProtectedPattern.Matches(text))
        {
            if (match.Success && match.Length > 0)
                spans.Add((match.Index, match.Index + match.Length));
        }

        return spans;
    }

    private static bool IsProtected(List<(int Start, int End)> spans, int start, int end)
    {
        foreach (var (spanStart, spanEnd) in spans)
        {
            if (start < spanEnd && end > spanStart)
                return true;
        }

        return false;
    }
}