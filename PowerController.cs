using System.Diagnostics;

namespace ZephyrRemote;

public class PowerController
{
    public bool Run(string action)
    {
        switch (action)
        {
            case "shutdown":
                Start("shutdown", "/s /t 0");
                return true;
            case "restart":
                Start("shutdown", "/r /t 0");
                return true;
            case "sleep":
                Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
                return true;
            case "lock":
                Start("rundll32.exe", "user32.dll,LockWorkStation");
                return true;
            default:
                return false;
        }
    }

    private void Start(string file, string args)
    {
        Process.Start(new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false });
    }
}
