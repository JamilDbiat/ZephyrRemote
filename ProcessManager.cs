using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ZephyrRemote;

public class ProcessManager
{
    private static readonly string[] Roots =
    {
        @"\program files\windowsapps\",
        @"\program files\",
        @"\program files (x86)\",
        @"\appdata\local\programs\",
        @"\appdata\local\",
        @"\appdata\roaming\"
    };

    private static readonly string[] HiddenNames =
    {
        "Windows", "Defender", "WebView2", "GameInput", "Touch Keyboard", "WidgetService", "VBCSCompiler", "ZephyrRemote"
    };

    private readonly Dictionary<string, List<int>> groups = new();
    private readonly Dictionary<string, string> paths = new();
    private readonly Dictionary<string, byte[]> icons = new();

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int id);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr handle, int flags, StringBuilder name, ref int size);

    public List<RunningApp> GetRunning()
    {
        groups.Clear();
        paths.Clear();

        int session = Process.GetCurrentProcess().SessionId;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLower();
        var entries = new List<(string Key, string Path, string Title, int Id)>();

        foreach (Process p in Process.GetProcesses())
        {
            if (p.SessionId != session || p.Id == Environment.ProcessId)
                continue;

            string path = PathOf(p.Id);
            if (path == "" || path.ToLower().StartsWith(windows))
                continue;

            string root = RootOf(path);
            string key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(root.ToLower())))[..12];
            string title = p.MainWindowHandle != IntPtr.Zero ? p.MainWindowTitle : "";
            entries.Add((key, path, title, p.Id));
        }

        var apps = new List<RunningApp>();
        foreach (var group in entries.GroupBy(e => e.Key))
        {
            var main = group.OrderBy(e => e.Path.Count(c => c == '\\')).ThenBy(e => e.Path.Length).First();
            var window = group.FirstOrDefault(e => e.Title != "");
            string name = Describe(main.Path);
            if (HiddenNames.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase)))
                continue;

            groups[group.Key] = group.Select(e => e.Id).ToList();
            paths[group.Key] = main.Path;

            apps.Add(new RunningApp
            {
                Id = group.Key,
                Name = name,
                Title = window.Title ?? "Running in background",
                HasWindow = window.Title != null
            });
        }

        return apps.OrderByDescending(a => a.HasWindow).ThenBy(a => a.Name).ToList();
    }

    public bool Close(string id)
    {
        if (!groups.ContainsKey(id))
            GetRunning();
        if (!groups.TryGetValue(id, out List<int>? ids))
            return false;

        List<Process> running = Process.GetProcesses().Where(p => ids.Contains(p.Id)).ToList();
        foreach (Process p in running.Where(p => p.MainWindowHandle != IntPtr.Zero))
            p.CloseMainWindow();

        Thread.Sleep(2000);

        bool closedAll = true;
        foreach (Process p in running)
        {
            p.Refresh();
            if (p.HasExited)
                continue;
            try
            {
                p.Kill(true);
            }
            catch (Exception)
            {
                closedAll = false;
            }
        }
        return closedAll;
    }

    public byte[] GetIcon(string id)
    {
        if (icons.TryGetValue(id, out byte[]? cached))
            return cached;
        if (!paths.ContainsKey(id))
            GetRunning();
        if (!paths.TryGetValue(id, out string? path) || !File.Exists(path))
            return Array.Empty<byte>();

        using Icon? icon = Icon.ExtractAssociatedIcon(path);
        if (icon == null)
            return Array.Empty<byte>();

        using Bitmap bitmap = icon.ToBitmap();
        using MemoryStream stream = new();
        bitmap.Save(stream, ImageFormat.Png);
        icons[id] = stream.ToArray();
        return icons[id];
    }

    private static string Describe(string path)
    {
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
        if (!string.IsNullOrWhiteSpace(info.FileDescription))
            return info.FileDescription.Trim();
        if (!string.IsNullOrWhiteSpace(info.ProductName))
            return info.ProductName.Trim();
        return Path.GetFileNameWithoutExtension(path);
    }

    private static string RootOf(string path)
    {
        string lower = path.ToLower();
        foreach (string root in Roots)
        {
            int i = lower.IndexOf(root);
            if (i < 0)
                continue;

            int start = i + root.Length;
            int end = lower.IndexOf('\\', start);
            return end < 0 ? path : path[..end];
        }

        string[] parts = path.Split('\\');
        return parts.Length > 3 ? string.Join("\\", parts.Take(3)) : path;
    }

    private static string PathOf(int id)
    {
        IntPtr handle = OpenProcess(0x1000, false, id);
        if (handle == IntPtr.Zero)
            return "";

        var name = new StringBuilder(1024);
        int size = name.Capacity;
        bool ok = QueryFullProcessImageName(handle, 0, name, ref size);
        CloseHandle(handle);
        return ok ? name.ToString() : "";
    }
}
