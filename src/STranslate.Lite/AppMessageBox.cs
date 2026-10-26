using System.Windows.Forms;

namespace STranslate.Lite;

/// <summary>
/// 统一的消息框入口，便于后续替换为仓库内的统一 UI。
/// </summary>
internal static class AppMessageBox
{
    public static void ShowError(string text, string caption, IWin32Window? owner = null)
    {
        if (owner is null)
            MessageBox.Show(text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
        else
            MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    public static void ShowInfo(string text, string caption, IWin32Window? owner = null)
    {
        if (owner is null)
            MessageBox.Show(text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
        else
            MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public static bool Confirm(string text, string caption, IWin32Window? owner = null)
    {
        var result = owner is null
            ? MessageBox.Show(text, caption, MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
            : MessageBox.Show(owner, text, caption, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        return result == DialogResult.OK;
    }
}