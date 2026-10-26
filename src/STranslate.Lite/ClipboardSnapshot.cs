using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace STranslate.Lite;

/// <summary>
/// 剪贴板深拷贝快照。除了纯文本，还会尽量保留图片、HTML、RTF、文件列表等格式。
/// 所有 Clipboard 调用都必须在 STA/UI 线程执行。
/// </summary>
internal sealed class ClipboardSnapshot : IDisposable
{
    private readonly DataObject? _data;
    private readonly bool _isEmpty;
    private readonly string? _plainText;
    private bool _disposed;

    private ClipboardSnapshot(DataObject? data, bool isEmpty, string? plainText)
    {
        _data = data;
        _isEmpty = isEmpty;
        _plainText = plainText;
    }

    /// <summary>
    /// 尝试抓取当前剪贴板。抓取失败时调用方必须放弃模拟 Ctrl+C，避免无法恢复。
    /// </summary>
    public static bool TryCapture(out ClipboardSnapshot snapshot)
    {
        snapshot = null!;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                var source = Clipboard.GetDataObject();
                if (source is null)
                {
                    snapshot = new ClipboardSnapshot(null, isEmpty: true, plainText: null);
                    return true;
                }

                var formats = source.GetFormats(autoConvert: false);
                var plainText = TryReadString(source, DataFormats.UnicodeText)
                    ?? TryReadString(source, DataFormats.Text);

                var data = new DataObject();
                var copiedFormatCount = 0;

                foreach (var format in formats)
                {
                    if (string.IsNullOrEmpty(format))
                        continue;

                    object? value;
                    try
                    {
                        value = source.GetData(format, autoConvert: false);
                    }
                    catch
                    {
                        // 某个延迟渲染格式不可用不应导致整个剪贴板丢失。
                        continue;
                    }

                    var copy = CloneValue(value);
                    if (copy is null)
                        continue;

                    try
                    {
                        data.SetData(format, autoConvert: false, copy);
                        copiedFormatCount++;

                        // 某些程序只暴露 System.Drawing.Bitmap 自定义格式，不提供标准 Bitmap。
                        // 额外补一份标准图片格式，保证恢复后 Clipboard.GetImage() 仍可读取。
                        if (copy is Image image)
                        {
                            data.SetImage(image);
                            copiedFormatCount++;
                        }
                    }
                    catch
                    {
                        // 某些自定义格式无法重新写入，忽略它但保留其它格式。
                    }
                }

                if (formats.Length == 0)
                {
                    if (plainText is null)
                    {
                        snapshot = new ClipboardSnapshot(null, isEmpty: true, plainText: null);
                        return true;
                    }

                    data.SetText(plainText);
                    copiedFormatCount = 1;
                }

                if (copiedFormatCount == 0)
                {
                    if (plainText is null)
                    {
                        Log.Error("剪贴板含有无法备份的格式，已取消本次取词以避免覆盖原内容。");
                        return false;
                    }

                    // 虽然自定义格式无法备份，但至少把纯文本放回 DataObject，恢复时不会清空剪贴板。
                    data.SetText(plainText);
                    copiedFormatCount = 1;
                }

                snapshot = new ClipboardSnapshot(data, isEmpty: false, plainText);
                return true;
            }
            catch (ExternalException) when (attempt < 5)
            {
                // 剪贴板正被其他进程占用，短暂等待后重试。
                Thread.Sleep(25);
            }
            catch (Exception ex)
            {
                Log.Error("读取剪贴板快照失败。", ex);
                return false;
            }
        }

        Log.Error("剪贴板持续被占用，已取消本次取词。");
        return false;
    }

    /// <summary>
    /// 恢复快照。全部格式失败时至少回退恢复纯文本。
    /// </summary>
    public bool Restore()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (_isEmpty)
                    Clipboard.Clear();
                else
                    Clipboard.SetDataObject(_data!, copy: true);

                return true;
            }
            catch (ExternalException) when (attempt < 5)
            {
                Thread.Sleep(30);
            }
            catch (Exception ex)
            {
                Log.Error("恢复剪贴板快照失败，将尝试仅恢复文本。", ex);
                break;
            }
        }

        if (string.IsNullOrEmpty(_plainText))
        {
            Log.Error("剪贴板恢复失败，且没有可回退的文本内容。");
            return false;
        }

        try
        {
            Clipboard.SetText(_plainText);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("回退恢复剪贴板文本也失败了。", ex);
            return false;
        }
    }

    private static string? TryReadString(IDataObject source, string format)
    {
        try
        {
            return source.GetData(format, autoConvert: false) as string;
        }
        catch
        {
            return null;
        }
    }

    private static object? CloneValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            string[] files => files.ToArray(),
            byte[] bytes => bytes.ToArray(),
            MemoryStream memoryStream => new MemoryStream(memoryStream.ToArray(), writable: false),
            Stream stream => CloneStream(stream),
            Bitmap bitmap => new Bitmap(bitmap),
            Image image => new Bitmap(image),
            _ => null
        };
    }

    private static MemoryStream CloneStream(Stream stream)
    {
        var buffer = new MemoryStream();
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (stream.CanSeek)
                stream.Position = 0;
            stream.CopyTo(buffer);
        }
        finally
        {
            if (stream.CanSeek)
                stream.Position = originalPosition;
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// 释放快照中深拷贝的 Bitmap / Stream。必须在剪贴板恢复完成后调用。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_data is null)
            return;

        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var data = (IDataObject)_data;
        foreach (var format in data.GetFormats(autoConvert: false))
        {
            try
            {
                var value = data.GetData(format, autoConvert: false);
                if (value is IDisposable disposable && disposed.Add(value))
                    disposable.Dispose();
            }
            catch
            {
                // 单个格式释放失败不影响其它资源。
            }
        }
    }
}