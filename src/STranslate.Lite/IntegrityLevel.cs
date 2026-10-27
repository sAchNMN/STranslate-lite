using System.Runtime.InteropServices;

namespace STranslate.Lite;

/// <summary>
/// 判断当前进程是否以低完整性级别（沙盒、AppContainer 等受限环境）运行。
/// 低权限下 Windows 会拦截模拟按键向普通权限窗口的投递，也禁止写入用户配置目录，
/// 表现为“取词不到文本”“保存配置失败”，需要给出可操作的提示而不是泛泛的失败。
/// </summary>
internal static class IntegrityLevel
{
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int LowIntegrityRid = 0x1000;

    public static bool IsLow()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
            return false;

        try
        {
            var buffer = Marshal.AllocHGlobal(256);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, 256, out _))
                    return false;

                var sid = Marshal.ReadIntPtr(buffer);
                if (sid == IntPtr.Zero)
                    return false;

                var subAuthorityCount = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (subAuthorityCount == 0)
                    return false;

                var rid = Marshal.ReadInt32(GetSidSubAuthority(sid, subAuthorityCount - 1));
                return rid <= LowIntegrityRid;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, int subAuthority);
}