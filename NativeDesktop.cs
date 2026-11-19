using System.Runtime.InteropServices;
using System.Text;

namespace ZephyrRemote;

public static class NativeDesktop
{
    private const uint GenericAll = 0x10000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr desktop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int length, out int needed);

    public static string InputDesktopName()
    {
        IntPtr desktop = OpenInputDesktop(0, false, GenericAll);
        if (desktop == IntPtr.Zero)
            return "Winlogon";

        StringBuilder name = new(256);
        GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _);
        CloseDesktop(desktop);
        return name.ToString();
    }

    public static void OnInputDesktop(Action action)
    {
        IntPtr original = GetThreadDesktop(GetCurrentThreadId());
        IntPtr input = OpenInputDesktop(0, false, GenericAll);
        if (input != IntPtr.Zero)
            SetThreadDesktop(input);

        try
        {
            action();
        }
        finally
        {
            SetThreadDesktop(original);
            if (input != IntPtr.Zero)
                CloseDesktop(input);
        }
    }
}
