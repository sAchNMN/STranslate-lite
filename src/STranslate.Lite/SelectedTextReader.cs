using System.Diagnostics;
using System.Windows.Forms;

namespace STranslate.Lite;

/// <summary>
/// 模拟 Ctrl+C 读取当前选中文本。读取前抓取完整剪贴板快照，读取后立即恢复。
/// </summary>
internal sealed class SelectedTextReader
{
    private const int CopySettleDelayMs = 100;
    private const int ClipboardUpdateTimeoutMs = 800;

    public async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!ClipboardSnapshot.TryCapture(out var snapshot))
            throw new InvalidOperationException("无法备份当前剪贴板，已取消取词。");

        var originalSequence = NativeMethods.GetClipboardSequenceNumber();
        var clipboardChanged = false;
        var copySent = false;

        try
        {
            if (!NativeMethods.SendCtrlC())
                throw new InvalidOperationException("模拟 Ctrl+C 失败，请检查输入权限。");

            copySent = true;

            // 关键要求：发送后固定等待 100ms，让目标程序完成剪贴板更新。
            await Task.Delay(CopySettleDelayMs, cancellationToken).ConfigureAwait(true);

            var stopwatch = Stopwatch.StartNew();
            while (NativeMethods.GetClipboardSequenceNumber() == originalSequence &&
                   stopwatch.ElapsedMilliseconds < ClipboardUpdateTimeoutMs)
            {
                await Task.Delay(15, cancellationToken).ConfigureAwait(true);
            }

            clipboardChanged = NativeMethods.GetClipboardSequenceNumber() != originalSequence;
            if (!clipboardChanged)
                return null;

            await Task.Delay(20, cancellationToken).ConfigureAwait(true);

            var text = TryReadText();
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return text.Trim();
        }
        finally
        {
            // 即使等待过程被取消，也要按剪贴板序列号判断是否真的写入过。
            if (copySent && !clipboardChanged)
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
}