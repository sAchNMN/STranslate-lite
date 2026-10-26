using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace STranslate.Lite;

/// <summary>
/// 无边框、半透明、永不激活的翻译浮窗。
/// 同时使用 WS_EX_NOACTIVATE、ShowWithoutActivation 和 WM_MOUSEACTIVATE=MA_NOACTIVATE 三重保护。
/// </summary>
internal sealed class PopupForm : Form
{
    private readonly System.Windows.Forms.Timer _lifecycleTimer;
    private readonly Font _titleFont;
    private readonly Font _bodyFont;
    private readonly Font _hintFont;
    private readonly StringFormat _bodyFormat;
    private readonly StringFormat _singleLineFormat;

    private string _title = string.Empty;
    private string _body = string.Empty;
    private string _hint = string.Empty;
    private Point _anchor;
    private Point _cursorAtShow;
    private DateTime _shownAt;
    private IntPtr _foregroundAtShow;
    private bool _wasInside;
    private int _outsideTicks;
    private IntPtr _escapeHook;
    private NativeMethods.HookProc? _escapeHookProc;
    private bool _dismissPosted;

    public PopupForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowIcon = false;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(18, 18, 18);
        ForeColor = Color.FromArgb(245, 245, 245);
        Opacity = 0.94;
        Text = "STranslate Lite";
        DoubleBuffered = true;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        _titleFont = CreateFont(8.5f, FontStyle.Regular);
        _bodyFont = CreateFont(11f, FontStyle.Regular);
        _hintFont = CreateFont(8f, FontStyle.Regular);

        _bodyFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisWord,
            FormatFlags = StringFormatFlags.LineLimit | StringFormatFlags.NoClip
        };

        _singleLineFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
        };

        _lifecycleTimer = new System.Windows.Forms.Timer
        {
            Interval = 100
        };
        _lifecycleTimer.Tick += OnLifecycleTick;
    }

    public event EventHandler? Dismissed;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= NativeMethods.WS_EX_NOACTIVATE |
                                  NativeMethods.WS_EX_TOOLWINDOW |
                                  NativeMethods.WS_EX_TOPMOST;
            parameters.ClassStyle |= NativeMethods.CS_DROPSHADOW;
            return parameters;
        }
    }

    public void ShowContent(Point anchor, string title, string body, string hint)
    {
        _anchor = anchor;
        _title = title;
        _body = body;
        _hint = hint;

        Log.Info($"浮窗显示内容：{title}。");

        var firstShow = !Visible;
        if (firstShow)
        {
            _foregroundAtShow = NativeMethods.GetForegroundWindow();
            _cursorAtShow = Cursor.Position;
            _shownAt = DateTime.UtcNow;
            _wasInside = false;
            _outsideTicks = 0;
        }

        _ = Handle; // 强制创建句柄，以便按当前显示器 DPI 测量文本。
        LayoutPopup();

        if (!Visible)
            Show();

        // 即使系统在 Show 时尝试激活窗口，也强制以“不激活”方式置顶。
        NativeMethods.SetWindowPos(
            Handle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE |
            NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOACTIVATE |
            NativeMethods.SWP_SHOWWINDOW);

        _lifecycleTimer.Start();
        StartEscapeHook();
        Invalidate();
    }

    public void Dismiss()
    {
        StopEscapeHook();
        if (!Visible)
            return;

        _lifecycleTimer.Stop();
        Hide();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 开始新一次取词时静默隐藏旧浮窗，不触发 Dismissed，避免影响新会话编号。
    /// </summary>
    public void HideSilently()
    {
        StopEscapeHook();
        _lifecycleTimer.Stop();
        if (Visible)
            Hide();
    }

    private void StartEscapeHook()
    {
        if (_escapeHook != IntPtr.Zero)
            return;

        _escapeHookProc ??= EscapeHookCallback;
        _escapeHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _escapeHookProc,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_escapeHook == IntPtr.Zero)
            Log.Error($"安装 Esc 键盘钩子失败，Win32 错误码：{Marshal.GetLastWin32Error()}。");
    }

    private void StopEscapeHook()
    {
        if (_escapeHook == IntPtr.Zero)
            return;

        _ = NativeMethods.UnhookWindowsHookEx(_escapeHook);
        _escapeHook = IntPtr.Zero;
    }

    private IntPtr EscapeHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 &&
                (wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN))
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (data.VirtualKey == NativeMethods.VK_ESCAPE && Visible && !_dismissPosted)
                {
                    _dismissPosted = true;
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _dismissPosted = false;
                            Log.Info("浮窗通过 Esc 关闭。");
                            Dismiss();
                        }));
                    }
                    catch
                    {
                        _dismissPosted = false;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("处理 Esc 键盘钩子时出错。", ex);
        }

        // 永远不拦截 Esc，让它继续传递给当前应用。
        return NativeMethods.CallNextHookEx(_escapeHook, nCode, wParam, lParam);
    }

    private void LayoutPopup()
    {
        using var graphics = CreateGraphics();
        var padding = Scale(18);
        var maxWidth = Scale(460);
        var minWidth = Scale(240);
        var maxBodyHeight = Scale(320);
        var contentWidth = maxWidth - padding * 2;

        var titleSize = graphics.MeasureString(_title, _titleFont, int.MaxValue, _singleLineFormat);
        var hintSize = string.IsNullOrEmpty(_hint)
            ? SizeF.Empty
            : graphics.MeasureString(_hint, _hintFont, int.MaxValue, _singleLineFormat);
        var bodySize = graphics.MeasureString(_body, _bodyFont, contentWidth, _bodyFormat);

        var desiredWidth = Math.Max(
            Math.Max(titleSize.Width, hintSize.Width),
            Math.Min(bodySize.Width, contentWidth)) + padding * 2;
        var width = Math.Clamp((int)Math.Ceiling(desiredWidth), minWidth, maxWidth);
        var finalContentWidth = width - padding * 2;
        var finalBodySize = graphics.MeasureString(_body, _bodyFont, finalContentWidth, _bodyFormat);

        var titleHeight = (int)Math.Ceiling(titleSize.Height);
        var hintHeight = string.IsNullOrEmpty(_hint) ? 0 : (int)Math.Ceiling(hintSize.Height);
        var bodyHeight = Math.Min(maxBodyHeight, Math.Max(0, (int)Math.Ceiling(finalBodySize.Height)));
        var titleGap = Scale(7);
        var hintGap = hintHeight > 0 ? Scale(10) : 0;
        var height = padding * 2 + titleHeight + titleGap + bodyHeight + hintGap + hintHeight;

        // 留出半个像素，避免边框被 Region 裁掉。
        height = Math.Max(height, Scale(64));
        var workingArea = Screen.FromPoint(_anchor).WorkingArea;
        var offsetX = Scale(16);
        var offsetY = Scale(20);

        var x = _anchor.X + offsetX;
        var y = _anchor.Y + offsetY;
        if (x + width > workingArea.Right - Scale(8))
            x = _anchor.X - width - offsetX;
        if (y + height > workingArea.Bottom - Scale(8))
            y = _anchor.Y - height - offsetY;

        x = Math.Clamp(x, workingArea.Left + Scale(8), Math.Max(workingArea.Left + Scale(8), workingArea.Right - width - Scale(8)));
        y = Math.Clamp(y, workingArea.Top + Scale(8), Math.Max(workingArea.Top + Scale(8), workingArea.Bottom - height - Scale(8)));

        Bounds = new Rectangle(x, y, width, height);
        UpdateRoundedRegion();
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
            return;

        using var path = CreateRoundedRectangle(new RectangleF(0, 0, Width, Height), Scale(11));
        var newRegion = new Region(path);
        var oldRegion = Region;
        Region = newRegion;
        oldRegion?.Dispose();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Windows 11 圆角；旧系统忽略返回值，仍由 Region 负责裁剪。
        var preference = NativeMethods.DWMWCP_ROUND;
        _ = NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref preference,
            sizeof(int));
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            m.Result = new IntPtr(NativeMethods.MA_NOACTIVATE);
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var padding = Scale(18);
        using var backgroundBrush = new SolidBrush(BackColor);
        graphics.FillRectangle(backgroundBrush, ClientRectangle);

        using var borderPath = CreateRoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Scale(11));
        using var borderPen = new Pen(Color.FromArgb(74, 74, 74), Math.Max(1f, ScaleF(1f)));
        graphics.DrawPath(borderPen, borderPath);

        var titleHeight = (int)Math.Ceiling(graphics.MeasureString(
            string.IsNullOrEmpty(_title) ? " " : _title,
            _titleFont,
            int.MaxValue,
            _singleLineFormat).Height);

        var y = padding;
        if (!string.IsNullOrEmpty(_title))
        {
            using var titleBrush = new SolidBrush(Color.FromArgb(156, 156, 156));
            graphics.DrawString(
                _title,
                _titleFont,
                titleBrush,
                new RectangleF(padding, y, Width - padding * 2, titleHeight),
                _singleLineFormat);
        }

        var bodyTop = y + titleHeight + Scale(7);
        var hintHeight = string.IsNullOrEmpty(_hint)
            ? 0
            : (int)Math.Ceiling(graphics.MeasureString(_hint, _hintFont, int.MaxValue, _singleLineFormat).Height);
        var bodyHeight = Height - bodyTop - padding - (hintHeight > 0 ? hintHeight + Scale(10) : 0);
        bodyHeight = Math.Max(0, bodyHeight);

        using var bodyBrush = new SolidBrush(Color.FromArgb(248, 248, 248));
        graphics.DrawString(
            _body,
            _bodyFont,
            bodyBrush,
            new RectangleF(padding, bodyTop, Width - padding * 2, bodyHeight),
            _bodyFormat);

        if (hintHeight > 0)
        {
            using var hintBrush = new SolidBrush(Color.FromArgb(118, 118, 118));
            graphics.DrawString(
                _hint,
                _hintFont,
                hintBrush,
                new RectangleF(padding, Height - padding - hintHeight, Width - padding * 2, hintHeight),
                _singleLineFormat);
        }
    }

    private void OnLifecycleTick(object? sender, EventArgs e)
    {
        if (!Visible)
            return;

        // 前 350ms 是保护期，避免浮窗刚出现就被无关的鼠标/焦点事件关闭。
        if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 350)
            return;

        // 浮窗从不接收键盘焦点，因此 Esc 通过仅在浮窗显示期间安装的键盘钩子捕获。
        // 钩子只观察、不拦截；若安装失败，则退回轮询按键状态。
        if (_escapeHook == IntPtr.Zero &&
            (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8001) != 0)
        {
            Log.Info("浮窗通过 Esc 轮询关闭。");
            Dismiss();
            return;
        }

        // 前台窗口发生变化即视为“失去焦点”，立即关闭。
        if (NativeMethods.GetForegroundWindow() != _foregroundAtShow)
        {
            Log.Info("浮窗检测到前台窗口变化，自动关闭。");
            Dismiss();
            return;
        }

        var cursor = Cursor.Position;
        var inside = Bounds.Contains(cursor);
        if (inside)
        {
            _wasInside = true;
            _outsideTicks = 0;
            return;
        }

        if (_wasInside)
        {
            // 鼠标曾经进入过浮窗，离开两拍后关闭。
            if (++_outsideTicks >= 2)
                Dismiss();
            return;
        }

        // 鼠标一开始就在浮窗外（浮窗按偏移量显示）：等鼠标明显移开触发点时再关闭。
        var deltaX = cursor.X - _cursorAtShow.X;
        var deltaY = cursor.Y - _cursorAtShow.Y;
        if (++_outsideTicks >= 5 && deltaX * deltaX + deltaY * deltaY > Scale(42) * Scale(42))
            Dismiss();
    }

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);

    private float ScaleF(float value) => value * DeviceDpi / 96f;

    private static Font CreateFont(float size, FontStyle style)
    {
        try
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }
        catch
        {
            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopEscapeHook();
            _lifecycleTimer.Stop();
            _lifecycleTimer.Dispose();
            _titleFont.Dispose();
            _bodyFont.Dispose();
            _hintFont.Dispose();
            _bodyFormat.Dispose();
            _singleLineFormat.Dispose();
            Region?.Dispose();
        }

        base.Dispose(disposing);
    }
}