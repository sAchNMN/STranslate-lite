using System.Threading;
using System.Windows.Forms;

namespace STranslate.Lite;

internal static class Program
{
    private const string MutexName = @"Local\STranslate.Lite.SingleInstance";
    private const string ActivationEventName = @"Local\STranslate.Lite.Activate";

    [STAThread]
    private static void Main(string[] args)
    {
        // 命令行诊断模式：不启动托盘、不占用单实例锁。
        if (args.Length > 0 &&
            (string.Equals(args[0], "--probe", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(args[0], "-p", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = EngineProbe.Run(args);
            return;
        }

        if (args.Length > 0 &&
            string.Equals(args[0], "--uicheck", StringComparison.OrdinalIgnoreCase))
        {
            Environment.ExitCode = UiSmokeTest.Run();
            return;
        }

        // 在创建任何窗口之前初始化 WinForms。DPI 模式由 csproj 的
        // ApplicationHighDpiMode=PerMonitorV2 统一提供，避免运行时与清单各写一份。
        ApplicationConfiguration.Initialize();

        // 同一会话只允许一个实例；再次启动时通知已有实例打开设置窗口。
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            return;
        }

        using var activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ActivationEventName);

        try
        {
            Application.SetDefaultFont(new Font("Microsoft YaHei UI", 9F));
        }
        catch
        {
            // 字体缺失时使用系统默认字体，不阻断启动。
        }

        Application.ThreadException += (_, e) =>
        {
            Log.Error("UI 线程出现未处理异常。", e.Exception);
            AppMessageBox.ShowError($"程序发生未处理异常：{e.Exception.Message}\n\n详细信息已写入日志。", "STranslate Lite");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
                Log.Error("后台线程出现未处理异常。", exception);
        };

        try
        {
            using var context = new TrayApplicationContext(activationEvent);
            Application.Run(context);
        }
        catch (Exception ex)
        {
            Log.Error("程序启动失败。", ex);
            AppMessageBox.ShowError($"程序启动失败：{ex.Message}\n\n详细信息已写入日志。", "STranslate Lite");
        }
    }

    private static void SignalExistingInstance()
    {
        // 已有实例创建事件前可能存在极短竞态，短暂重试。
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                activationEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
            catch
            {
                return;
            }
        }
    }
}