using System.Diagnostics;

namespace ZephyrRemote;

public static class AgentKey
{
    public const int Port = 5051;
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ZephyrRemote");
    private static readonly string FilePath = Path.Combine(Folder, "agent.key");

    public static void Write(string key)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, key);
        ProcessStartInfo info = new("icacls.exe", $"\"{FilePath}\" /inheritance:r /grant:r *S-1-5-18:F *S-1-5-32-544:F")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        Process.Start(info)?.WaitForExit();
    }

    public static string Read()
    {
        return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : "";
    }
}
