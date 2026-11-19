using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZephyrRemote;

public static class SessionLauncher
{
    private const uint TokenAllAccess = 0xF01FF;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const int TokenSessionId = 12;
    private const uint CreateNoWindow = 0x08000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Cb;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2;
        public IntPtr Reserved3, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [DllImport("kernel32.dll")]
    public static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int level, int type, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr token, int infoClass, ref uint info, int length);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(IntPtr token, string? application, string commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInformation info);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static Process? Start(string exe, string arguments, uint session)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAllAccess, out IntPtr own))
            return null;

        bool ok = DuplicateTokenEx(own, TokenAllAccess, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out IntPtr token);
        CloseHandle(own);
        if (!ok)
            return null;

        SetTokenInformation(token, TokenSessionId, ref session, sizeof(uint));

        StartupInfo startup = new() { Cb = Marshal.SizeOf<StartupInfo>(), Desktop = @"winsta0\default" };
        string commandLine = $"\"{exe}\" {arguments}";
        ok = CreateProcessAsUser(token, null, commandLine, IntPtr.Zero, IntPtr.Zero, false, CreateNoWindow, IntPtr.Zero,
            Path.GetDirectoryName(exe), ref startup, out ProcessInformation info);
        CloseHandle(token);
        if (!ok)
            return null;

        CloseHandle(info.Thread);
        CloseHandle(info.Process);
        return Process.GetProcessById(info.ProcessId);
    }
}
