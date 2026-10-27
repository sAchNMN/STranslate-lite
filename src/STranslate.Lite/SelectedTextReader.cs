using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace STranslate.Lite;

/// <summary>
/// 取词失败的具体原因。用于给用户展示可操作的建议，而不是一律提示“没有选中文字”。
/// </summary>
internal enum TextCaptureFailure
{
    None,
    NoSelection,
    CopyRejected,
    ClipboardBusy,
    LowIntegrity,
    Error
}

/// <summary>
/// 一次取词结果。<see cref="Method"/> 记录实际生效的取词方式，便于日志排查。
/// </summary>
internal sealed record TextCaptureResult(
    string? Text,
    string? Method = null,
    TextCaptureFailure Failure = TextCaptureFailure.None,
    string? Message = null)
{
    public bool Success => !string.IsNullOrWhiteSpace(Text);

    public static TextCaptureResult Ok(string text, string method) => new(text, method);

    public static TextCaptureResult Fail(TextCaptureFailure failure, string? message = null) =>
        new(null, null, failure, message);
}

/// <summary>
/// 取词器。浏览器优先用 UI Automation 直接读取选区，失败后再回退到“模拟 Ctrl+C + 剪贴板”；
/// 其他程序保持 Ctrl+C 优先，失败时再用 UI Automation 兜底，避免改变已有复制行为。
/// <para>
/// UI Automation 不修改剪贴板、不需要按键注入，因此不会受浏览器 Alt 菜单、剪贴板占用、
/// 富文本格式无法恢复等问题影响。
/// </para>
/// </summary>
internal sealed class SelectedTextReader
{
    private const int UiAutomationTimeoutMs = 550;
    private const int UiAutomationRetryTimeoutMs = 650;
    private const int CopySettleDelayMs = 100;
    private const int ClipboardUpdateTimeoutMs = 900;
    private const int ClipboardRetryTimeoutMs = 550;
    private const int ClipboardReadDelayMs = 35;

    /// <summary>
    /// 这些进程的窗口优先用 UI Automation 取词。浏览器对模拟 Ctrl+C 最敏感：
    /// Alt 菜单、无障碍树初始化、页面焦点都可能让复制的按键落空。
    /// 其他程序保持原有的 Ctrl+C 优先行为，避免改变 Excel / Word 等程序的复制结果。
    /// </summary>
    private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "msedgewebview2", "firefox", "brave", "opera", "vivaldi",
        "chromium", "iexplore", "360se", "360chrome", "qqbrowser", "sogouexplorer",
        "liebao", "2345explorer", "maxthon"
    };

    public async Task<TextCaptureResult> ReadAsync(
        CancellationToken cancellationToken,
        int triggerVirtualKey = 0,
        bool triggerUsesAlt = false)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var isBrowser = IsBrowserWindow(foreground);
        Log.Info($"开始取词，前台窗口：{DescribeWindow(foreground)}" +
                 $"{(isBrowser ? "（浏览器）" : string.Empty)}。");

        var probe = default(UiSelectionProbe);

        // 1. 浏览器首选 UI Automation：直接读取无障碍选区，不注入按键、不碰剪贴板。
        if (isBrowser)
        {
            probe = await UiAutomationSelectionReader
                .TryReadAsync(UiAutomationTimeoutMs, cancellationToken, browserOnly: isBrowser)
                .ConfigureAwait(true);

            if (probe.HasText)
            {
                Log.Info($"UI 自动化读取选区成功，长度 {probe.Text!.Length}。");
                return TextCaptureResult.Ok(probe.Text, "UI Automation");
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        // 2. 非浏览器保持 Ctrl+C 优先；浏览器在 UIA 失败时也回退到 Ctrl+C。
        var result = await ReadViaClipboardAsync(
                triggerVirtualKey,
                triggerUsesAlt,
                probe,
                isBrowser,
                cancellationToken)
            .ConfigureAwait(true);

        if (result.Success)
            return result;

        // 3. 仍有失败时，再给 UI Automation 一次机会。
        //    非浏览器第一次没有走 UIA，这里可以兜底；浏览器第一次可能还没来得及
        //    构建无障碍树，或者焦点元素与正文 Document 不是同一个，第二次通常更完整。
        var retry = await UiAutomationSelectionReader
            .TryReadAsync(UiAutomationRetryTimeoutMs, cancellationToken, browserOnly: isBrowser)
            .ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();

        if (retry.HasText)
        {
            Log.Info($"UI 自动化重试读取选区成功，长度 {retry.Text!.Length}。");
            return TextCaptureResult.Ok(retry.Text, "UI Automation");
        }

        if (retry.TextPatternAvailable && !probe.TextPatternAvailable)
            probe = new UiSelectionProbe(null, true);

        // TextPattern 可用但读不到文字，基本可以确定当前焦点处没有选中内容。
        if (probe.TextPatternAvailable && result.Failure == TextCaptureFailure.CopyRejected)
        {
            return TextCaptureResult.Fail(
                TextCaptureFailure.NoSelection,
                "当前焦点位置没有选中文本。请先用鼠标选中文字，再按触发键。");
        }

        return result;
    }

    private async Task<TextCaptureResult> ReadViaClipboardAsync(
        int triggerVirtualKey,
        bool triggerUsesAlt,
        UiSelectionProbe probe,
        bool isBrowser,
        CancellationToken cancellationToken)
    {
        if (!ClipboardSnapshot.TryCapture(out var snapshot))
        {
            return TextCaptureResult.Fail(
                TextCaptureFailure.ClipboardBusy,
                "无法备份当前剪贴板，可能正被其他程序占用。请稍后重试。");
        }

        var originalSequence = NativeMethods.GetClipboardSequenceNumber();
        var originalText = snapshot.PlainText;
        var clipboardChanged = false;

        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var timeout = attempt == 0
                    ? ClipboardUpdateTimeoutMs
                    : ClipboardRetryTimeoutMs;

                if (!NativeMethods.SendCtrlC(triggerVirtualKey))
                {
                    if (IntegrityLevel.IsLow())
                        return LowIntegrityResult();

                    return TextCaptureResult.Fail(
                        TextCaptureFailure.CopyRejected,
                        "无法向目标程序注入复制按键，请确认程序以普通权限运行。");
                }

                // 关键要求：发送后先固定等待，让目标程序完成复制；然后再轮询序列号。
                await Task.Delay(CopySettleDelayMs, cancellationToken).ConfigureAwait(true);

                clipboardChanged = await WaitForClipboardChangeAsync(
                        originalSequence,
                        originalText,
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(true);

                if (!clipboardChanged)
                {
                    Log.Info($"模拟 Ctrl+C 后剪贴板未发生变化（第 {attempt + 1} 次），前台窗口：" +
                             $"{DescribeWindow(NativeMethods.GetForegroundWindow())}。");

                    // Alt 组合键（例如默认的 Ctrl+Alt+T、用户的 Alt+X）在浏览器和
                    // 传统 Win32 程序中可能激活菜单栏，键盘焦点随之离开正文。
                    // 此时先发 Esc 关闭菜单，再重试 Ctrl+C，往往就能正常复制。
                    if (attempt == 0 && triggerUsesAlt)
                    {
                        Log.Info("检测到 Alt 组合键触发，尝试用 Esc 关闭可能已打开的菜单后重试。");
                        if (NativeMethods.SendEscape())
                            await Task.Delay(80, cancellationToken).ConfigureAwait(true);
                    }

                    continue;
                }

                await Task.Delay(ClipboardReadDelayMs, cancellationToken).ConfigureAwait(true);

                var text = TryReadText();
                if (string.IsNullOrWhiteSpace(text))
                {
                    Log.Info("剪贴板已更新，但未读到文本内容。");
                    return TextCaptureResult.Fail(
                        TextCaptureFailure.NoSelection,
                        "剪贴板已更新，但目标程序写入的不是文本内容。");
                }

                Log.Info($"Ctrl+C 读取选区成功，长度 {text.Length}。");
                return TextCaptureResult.Ok(text.Trim(), "Ctrl+C");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("通过剪贴板读取选中文本失败。", ex);
            return TextCaptureResult.Fail(
                TextCaptureFailure.Error,
                "读取剪贴板时发生异常，详细信息已写入日志。");
        }
        finally
        {
            // 即使等待过程被取消，也要按剪贴板序列号判断是否真的写入过。
            if (!clipboardChanged)
                clipboardChanged = NativeMethods.GetClipboardSequenceNumber() != originalSequence;

            try
            {
                if (clipboardChanged && !snapshot.Restore())
                    Log.Error("本次取词后剪贴板未能恢复。");
            }
            finally
            {
                snapshot.Dispose();
            }
        }

        if (IntegrityLevel.IsLow())
            return LowIntegrityResult();

        // TextPattern 可用但仍没有文字：基本可以确定当前焦点处没有选中内容。
        // 否则更可能是目标程序不响应注入按键。
        var failure = probe.TextPatternAvailable
            ? TextCaptureFailure.NoSelection
            : TextCaptureFailure.CopyRejected;

        return TextCaptureResult.Fail(
            failure,
            failure == TextCaptureFailure.NoSelection
                ? "当前焦点位置没有选中文本。请先用鼠标选中文字，再按触发键。"
                : isBrowser
                    ? "目标程序没有响应复制按键。浏览器可能把 Alt 当成了菜单键；" +
                      "建议改用 Ctrl+Shift 组合键。也可以查看日志确认前台程序。"
                    : "没有读取到选中文本。请确认文字仍处于选中状态；" +
                      "如果目标程序以管理员权限运行，请以管理员身份启动本程序。");
    }

    private static TextCaptureResult LowIntegrityResult() =>
        TextCaptureResult.Fail(
            TextCaptureFailure.LowIntegrity,
            "当前程序以受限权限运行，系统拦截了发往目标程序的复制按键。" +
            "请在“资源管理器”中直接双击 STranslate.Lite.exe 启动后再试。");

    private static async Task<bool> WaitForClipboardChangeAsync(
        uint originalSequence,
        string? originalText,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            if (NativeMethods.GetClipboardSequenceNumber() != originalSequence)
                return true;

            await Task.Delay(20, cancellationToken).ConfigureAwait(true);
        }

        // 少数程序会写回与原来完全相同的文本，序列号可能不变；
        // 用内容兜底，避免把成功的复制误判为失败。
        var current = TryReadText();
        return !string.IsNullOrWhiteSpace(current) &&
               !string.Equals(current, originalText, StringComparison.Ordinal);
    }

    private static string? TryReadText()
    {
        try
        {
            var text = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (!string.IsNullOrEmpty(text))
                return text;
        }
        catch (Exception ex)
        {
            Log.Error("读取 Unicode 剪贴板文本失败。", ex);
        }

        try
        {
            return Clipboard.GetText(TextDataFormat.Text);
        }
        catch (Exception ex)
        {
            Log.Error("读取 ANSI 剪贴板文本失败。", ex);
            return null;
        }
    }

    /// <summary>
    /// 判断前台窗口是否属于浏览器。只用于决定“UIA 优先还是 Ctrl+C 优先”，
    /// 判断失败时按非浏览器处理，保持原有行为。
    /// </summary>
    private static bool IsBrowserWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return false;

        var (className, processName) = GetWindowInfo(window);
        if (className.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("MozillaWindowClass", StringComparison.OrdinalIgnoreCase) ||
            className.Equals("IEFrame", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return BrowserProcessNames.Contains(processName);
    }

    /// <summary>
    /// 只记录窗口类名与进程名，不记录窗口标题，避免把用户的文档名写进日志。
    /// </summary>
    private static string DescribeWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return "不可用";

        var (className, processName) = GetWindowInfo(window);
        return string.IsNullOrEmpty(processName) || processName == "?"
            ? $"class={className}"
            : $"class={className}, process={processName}";
    }

    private static (string ClassName, string ProcessName) GetWindowInfo(IntPtr window)
    {
        var className = "?";
        try
        {
            var buffer = new StringBuilder(256);
            if (NativeMethods.GetClassName(window, buffer, buffer.Capacity) > 0)
                className = buffer.ToString();
        }
        catch
        {
            // 窗口可能在读取过程中关闭。
        }

        var processName = "?";
        try
        {
            _ = NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId != 0)
                processName = Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            // 目标进程可能已退出，或当前权限无法查询。
        }

        return (className, processName);
    }
}