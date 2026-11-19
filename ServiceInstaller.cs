using System.Diagnostics;
using System.ServiceProcess;

namespace ZephyrRemote;

public static class ServiceInstaller
{
    public static void EnsureInstalled()
    {
        string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ZephyrRemote");
        string sourceExe = Environment.ProcessPath!;
        string targetExe = Path.Combine(target, Path.GetFileName(sourceExe));

        if (string.Equals(Path.GetDirectoryName(sourceExe), target, StringComparison.OrdinalIgnoreCase))
            return;

        bool exists = ServiceController.GetServices().Any(s => s.ServiceName == HelperService.Name);
        bool outdated = !File.Exists(targetExe) || File.GetLastWriteTimeUtc(sourceExe) != File.GetLastWriteTimeUtc(targetExe);

        if (exists && !outdated)
        {
            using ServiceController current = new(HelperService.Name);
            if (current.Status == ServiceControllerStatus.Stopped)
                current.Start();
            return;
        }

        if (exists)
        {
            using ServiceController service = new(HelperService.Name);
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
            Thread.Sleep(1000);
        }

        CopyFolder(AppContext.BaseDirectory, target);

        if (!exists)
            Run("sc.exe", $"create {HelperService.Name} binPath= \"\\\"{targetExe}\\\" --service\" start= auto obj= LocalSystem DisplayName= \"Zephyr Remote Helper\"");

        Run("sc.exe", $"start {HelperService.Name}");
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static void Run(string file, string arguments)
    {
        ProcessStartInfo info = new(file, arguments) { CreateNoWindow = true, UseShellExecute = false };
        Process.Start(info)?.WaitForExit(15000);
    }
}
