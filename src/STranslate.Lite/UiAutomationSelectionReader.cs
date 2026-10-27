using System.Diagnostics;
using System.Text;
using System.Windows.Automation;

namespace STranslate.Lite;

/// <summary>
/// 一次 UI Automation 选区探测结果。
/// <para>
/// <see cref="TextPatternAvailable"/> 表示目标程序是否暴露了 TextPattern。
/// 如果暴露了但 <see cref="Text"/> 为空，通常说明当前焦点处确实没有选中文本；
/// 如果未暴露，则说明该程序不支持无障碍读取，需要回退到模拟 Ctrl+C。
/// </para>
/// </summary>
internal readonly record struct UiSelectionProbe(string? Text, bool TextPatternAvailable)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// 通过 Windows UI Automation 直接读取当前选区。
/// 浏览器优先使用该方式：它不修改剪贴板、不需要模拟按键，也不会受到 Alt 菜单键的影响；
/// 其他程序在 Ctrl+C 失败后也会用它兜底。
/// </summary>
/// <remarks>
/// UI Automation 的跨进程调用可能被目标程序阻塞，因此每次探测都放在线程池中执行，
/// 并设置硬超时。超时后调用方立即回退到 Ctrl+C，不会卡住托盘 UI 线程。
/// </remarks>
internal static class UiAutomationSelectionReader
{
    private const int MaxTextLength = 5000;
    private const int MaxAncestorDepth = 8;
    private const int MaxCandidates = 12;

    /// <summary>
    /// 允许多少个探测同时挂起。正常情况下一次取词只会有一个探测；
    /// 上限用于防止目标程序无响应时反复触发造成线程堆积。
    /// </summary>
    private const int MaxConcurrentProbes = 2;

    private static int _activeProbes;

    /// <summary>
    /// 在后台线程读取当前选区，带硬超时；超时或失败时返回空结果，由调用方回退到 Ctrl+C。
    /// </summary>
    /// <param name="timeoutMs">本次探测允许占用的最长时间。</param>
    /// <param name="cancellationToken">取消信号。</param>
    /// <param name="browserOnly">
    /// 为 true 时只从前台窗口的正文 Document 读取，避免把浏览器地址栏、工具栏里遗留的选区
    /// 误当成网页选区；焦点元素本身仍然会先被读取，因此地址栏/输入框内的选区不受影响。
    /// </param>
    public static async Task<UiSelectionProbe> TryReadAsync(
        int timeoutMs,
        CancellationToken cancellationToken,
        bool browserOnly = false)
    {
        if (Interlocked.Increment(ref _activeProbes) > MaxConcurrentProbes)
        {
            Interlocked.Decrement(ref _activeProbes);
            Log.Info("UI 自动化探测仍在等待上一次调用，本次直接回退到 Ctrl+C。");
            return default;
        }

        Task<UiSelectionProbe>? probe = null;
        try
        {
            probe = Task.Run(() => TryReadCore(browserOnly));
            return await probe
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Log.Info($"UI 自动化读取选区超时（{timeoutMs}ms），回退到 Ctrl+C。");
            return default;
        }
        catch (OperationCanceledException)
        {
            return default;
        }
        catch (Exception ex)
        {
            Log.Error("UI 自动化读取选区失败，回退到 Ctrl+C。", ex);
            return default;
        }
        finally
        {
            // 超时后探测线程可能仍在运行。此时不能立刻放开并发额度，
            // 否则每次触发都会再堆一个线程；等它真正结束后再释放。
            if (probe is null || probe.IsCompleted)
            {
                Interlocked.Decrement(ref _activeProbes);
            }
            else
            {
                _ = probe.ContinueWith(
                    _ => Interlocked.Decrement(ref _activeProbes),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    private static UiSelectionProbe TryReadCore(bool browserOnly)
    {
        var textPatternAvailable = false;

        // 1. 优先读取键盘焦点所在元素及其祖先。浏览器通常把选区挂在 Document 上，
        //    而 FocusedElement 可能是它的子节点。
        var focused = TryGetFocusedElement();
        if (focused is not null)
        {
            var text = TryReadFromElementChain(focused, ref textPatternAvailable);
            if (!string.IsNullOrWhiteSpace(text))
                return new UiSelectionProbe(text, true);
        }

        // 2. 某些程序（尤其是菜单激活后的浏览器）不会把 Document 暴露为焦点元素。
        //    改为从前台窗口向下查找，仍然可以直接读到选区。
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            return new UiSelectionProbe(null, textPatternAvailable);

        AutomationElement? root;
        try
        {
            root = AutomationElement.FromHandle(foreground);
        }
        catch (Exception ex)
        {
            Log.Info($"UI 自动化无法访问前台窗口：{ex.GetType().Name}。");
            return new UiSelectionProbe(null, textPatternAvailable);
        }

        if (root is null)
            return new UiSelectionProbe(null, textPatternAvailable);

        var rootText = TryReadSelection(root, ref textPatternAvailable);
        if (!string.IsNullOrWhiteSpace(rootText))
            return new UiSelectionProbe(rootText, true);

        // 3. 正文 Document 永远优先。浏览器地址栏也是可编辑控件，如果先遍历 Edit，
        //    可能把地址栏里遗留的选区当成网页选区，翻译出错误的 URL。
        var documentText = TryReadCandidates(
            root,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
            ref textPatternAvailable);
        if (!string.IsNullOrWhiteSpace(documentText))
            return new UiSelectionProbe(documentText, true);

        // 浏览器到此为止；正文没有选区时交给 Ctrl+C 回退，避免误读地址栏/工具栏。
        if (browserOnly)
            return new UiSelectionProbe(null, textPatternAvailable);

        // 4. 非浏览器继续查找 Edit 以及把 TextPattern 挂在子元素上的程序。
        var editText = TryReadCandidates(
            root,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            ref textPatternAvailable);
        if (!string.IsNullOrWhiteSpace(editText))
            return new UiSelectionProbe(editText, true);

        var textElementText = TryReadCandidates(
            root,
            new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true),
            ref textPatternAvailable);
        if (!string.IsNullOrWhiteSpace(textElementText))
            return new UiSelectionProbe(textElementText, true);

        return new UiSelectionProbe(null, textPatternAvailable);
    }

    private static string? TryReadCandidates(
        AutomationElement root,
        Condition condition,
        ref bool textPatternAvailable)
    {
        AutomationElementCollection candidates;
        try
        {
            candidates = root.FindAll(TreeScope.Descendants, condition);
        }
        catch (Exception ex)
        {
            Log.Info($"UI 自动化遍历前台窗口失败：{ex.GetType().Name}。");
            return null;
        }

        var inspected = 0;
        foreach (AutomationElement candidate in candidates)
        {
            if (++inspected > MaxCandidates)
                break;

            var text = TryReadSelection(candidate, ref textPatternAvailable);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static AutomationElement? TryGetFocusedElement()
    {
        try
        {
            return AutomationElement.FocusedElement;
        }
        catch (Exception ex)
        {
            Log.Info($"UI 自动化无法读取焦点元素：{ex.GetType().Name}。");
            return null;
        }
    }

    private static string? TryReadFromElementChain(
        AutomationElement element,
        ref bool textPatternAvailable)
    {
        var current = element;
        try
        {
            for (var depth = 0; depth < MaxAncestorDepth && current is not null; depth++)
            {
                var text = TryReadSelection(current, ref textPatternAvailable);
                if (!string.IsNullOrWhiteSpace(text))
                    return text;

                current = TreeWalker.ControlViewWalker.GetParent(current);
            }
        }
        catch (Exception ex)
        {
            Log.Info($"UI 自动化读取选区祖先链失败：{ex.GetType().Name}。");
        }

        return null;
    }

    private static string? TryReadSelection(
        AutomationElement element,
        ref bool textPatternAvailable)
    {
        try
        {
            if (element.Current.IsPassword)
                return null;

            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject) ||
                patternObject is not TextPattern pattern)
            {
                return null;
            }

            // 只要目标程序支持 TextPattern，就记录下来。调用方可以据此区分
            // “没有选中文字”和“程序不支持无障碍读取、复制又失败”。
            textPatternAvailable = true;

            var ranges = pattern.GetSelection();
            if (ranges.Length == 0)
                return null;

            var builder = new StringBuilder();
            foreach (var range in ranges)
            {
                if (builder.Length > 0)
                    builder.AppendLine();

                // 多读一个字符用于判断是否超长，避免先读完整篇文档。
                var chunk = range.GetText(MaxTextLength + 1);
                if (string.IsNullOrEmpty(chunk))
                    continue;

                if (chunk.Length > MaxTextLength)
                    chunk = chunk[..MaxTextLength];

                builder.Append(chunk);
                if (builder.Length >= MaxTextLength)
                    break;
            }

            var text = builder.ToString();
            if (text.Length > MaxTextLength)
                text = text[..MaxTextLength];

            return Normalize(text);
        }
        catch (ElementNotAvailableException)
        {
            // 目标窗口在读取过程中关闭，属于正常竞态。
            return null;
        }
        catch (Exception ex)
        {
            // 单个元素的 UIA 提供程序异常不应中断整个探测。
            Debug.WriteLine($"UI Automation selection read failed: {ex}");
            return null;
        }
    }

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // 不同 UIA 提供程序对换行的表示不一致，统一成 \n 便于翻译与显示。
        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        return normalized.Length == 0 ? null : normalized;
    }
}
