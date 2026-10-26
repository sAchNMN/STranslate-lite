using System.Windows.Forms;

namespace STranslate.Lite;

/// <summary>
/// 串行协调一次完整取词：全屏判断 -> 备份剪贴板 -> Ctrl+C -> 恢复剪贴板 -> 翻译 -> 展示。
/// </summary>
internal sealed class TriggerCoordinator : IDisposable
{
    private const int MaxTextLength = 5000;

    private readonly Func<AppSettings> _settingsProvider;
    private readonly PopupForm _popup;
    private readonly SelectedTextReader _reader;
    private readonly TranslationService _translator;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private int _session;
    private bool _disposed;

    public TriggerCoordinator(Func<AppSettings> settingsProvider, PopupForm popup)
    {
        _settingsProvider = settingsProvider;
        _popup = popup;
        _reader = new SelectedTextReader();
        _translator = new TranslationService();
        _popup.Dismissed += OnPopupDismissed;
    }

    public void RequestTrigger()
    {
        if (_disposed)
            return;

        // 上一次翻译尚未结束时直接忽略，不排队堆积请求。
        if (!_gate.Wait(0))
            return;

        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var session = Interlocked.Increment(ref _session);
        var anchor = Cursor.Position;
        _popup.HideSilently();

        try
        {
            var settings = _settingsProvider();
            if (settings.IgnoreFullscreen && FullscreenDetector.IsForegroundWindowFullscreen())
            {
                Log.Info("前台正在全屏运行，已跳过本次翻译。");
                return;
            }

            string? selectedText;
            try
            {
                selectedText = await _reader.ReadAsync(_shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("读取选中文本失败。", ex);
                ShowIfCurrent(session, anchor, "取词失败", GetFriendlyError(ex));
                return;
            }

            if (session != Volatile.Read(ref _session))
                return;

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                ShowIfCurrent(
                    session,
                    anchor,
                    "没有获取到选中的文本",
                    "请先在任意程序中选中文字，再按触发键。");
                return;
            }

            var truncated = selectedText.Length > MaxTextLength;
            var text = truncated ? selectedText[..MaxTextLength] : selectedText;
            Log.Info($"已获取选中文本，长度 {text.Length}，开始翻译。");

            _popup.ShowContent(anchor, "STranslate Lite", "正在翻译…", "Esc / 移开鼠标关闭");

            TranslationResult result;
            try
            {
                result = await _translator.TranslateAsync(text, settings, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("翻译失败。", ex);
                ShowIfCurrent(session, anchor, "翻译失败", GetFriendlyError(ex));
                return;
            }

            if (session != Volatile.Read(ref _session))
                return;

            var title = $"{result.TranslatorName} · " +
                        $"{LanguageUtil.GetDisplayName(result.SourceLanguage)} → " +
                        $"{LanguageUtil.GetDisplayName(result.TargetLanguage)}";
            var hint = truncated
                ? "原文过长，已截断 · Esc / 移开鼠标关闭"
                : "Esc / 移开鼠标关闭";

            _popup.ShowContent(anchor, title, result.Text, hint);
        }
        catch (Exception ex)
        {
            Log.Error("处理取词请求时发生未预期异常。", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ShowIfCurrent(int session, Point anchor, string title, string body)
    {
        if (session != Volatile.Read(ref _session))
            return;

        _popup.ShowContent(anchor, title, body, "Esc / 移开鼠标关闭");
    }

    private void OnPopupDismissed(object? sender, EventArgs e)
    {
        // 用户已经关闭浮窗，迟到的翻译结果不应再次把窗口弹出来。
        Interlocked.Increment(ref _session);
    }

    private static string GetFriendlyError(Exception exception)
    {
        return exception switch
        {
            TimeoutException => exception.Message,
            InvalidOperationException => exception.Message,
            _ => "发生未知错误，详细信息已写入日志。"
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _popup.Dismissed -= OnPopupDismissed;
        _shutdown.Cancel();
        _translator.Dispose();
        _shutdown.Dispose();
    }
}