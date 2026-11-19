using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace ZephyrRemote;

public class AppScanner
{
    private readonly ConcurrentDictionary<string, byte[]> icons = new();
    private readonly string iconFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZephyrRemote", "icons");
    private readonly Dictionary<string, string> targets = new();
    private readonly object sync = new();
    private Dictionary<string, AppEntry> apps = new();
    private List<AppEntry> list = new();
    private DateTime scannedAt = DateTime.MinValue;
    private Task scanTask = Task.CompletedTask;

    public void Refresh()
    {
        lock (sync)
        {
            if (!scanTask.IsCompleted)
                return;

            TaskCompletionSource done = new();
            Thread thread = new(() =>
            {
                Scan();
                done.SetResult();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            scanTask = done.Task;
        }
    }

    public List<AppEntry> GetApps()
    {
        if (scannedAt == DateTime.MinValue)
        {
            Refresh();
            scanTask.Wait();
        }
        else if (DateTime.UtcNow - scannedAt > TimeSpan.FromSeconds(30))
        {
            Refresh();
        }
        return list;
    }

    private void Scan()
    {
        Dictionary<string, AppEntry> found = new();
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        dynamic? shell = null;
        Type? type = Type.GetTypeFromProgID("WScript.Shell");
        if (type != null)
            shell = Activator.CreateInstance(type);

        string[] folders =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };

        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };

        foreach (string folder in folders)
        {
            if (!Directory.Exists(folder))
                continue;

            foreach (string file in Directory.EnumerateFiles(folder, "*", options))
            {
                string ext = System.IO.Path.GetExtension(file).ToLower();
                if (ext != ".lnk" && ext != ".url")
                    continue;

                string name = System.IO.Path.GetFileNameWithoutExtension(file);
                if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ext == ".lnk" && IsBroken(file, shell))
                    continue;
                if (!names.Add(name))
                    continue;

                string id = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(file)))[..12];
                found[id] = new AppEntry { Id = id, Name = name, Path = file };
            }
        }

        apps = found;
        list = found.Values.OrderBy(a => a.Name).ToList();
        scannedAt = DateTime.UtcNow;
    }

    private bool IsBroken(string shortcut, dynamic? shell)
    {
        if (!targets.TryGetValue(shortcut, out string? target))
        {
            target = shell == null ? "" : (string)shell.CreateShortcut(shortcut).TargetPath;
            targets[shortcut] = target;
        }
        return target != "" && !File.Exists(target) && !Directory.Exists(target);
    }

    public bool Launch(string id)
    {
        if (!apps.ContainsKey(id))
        {
            Refresh();
            scanTask.Wait();
        }
        if (!apps.TryGetValue(id, out AppEntry? app))
            return false;

        Process.Start("explorer.exe", "\"" + app.Path + "\"");
        return true;
    }

    public byte[] GetIcon(string id)
    {
        if (icons.TryGetValue(id, out byte[]? cached))
            return cached;
        string saved = System.IO.Path.Combine(iconFolder, id + ".png");
        if (File.Exists(saved))
            return icons[id] = File.ReadAllBytes(saved);
        if (!apps.ContainsKey(id))
        {
            Refresh();
            scanTask.Wait();
        }
        if (!apps.TryGetValue(id, out AppEntry? app))
            return Array.Empty<byte>();

        using Icon? icon = Icon.ExtractAssociatedIcon(app.Path);
        if (icon == null)
            return Array.Empty<byte>();

        using Bitmap bitmap = icon.ToBitmap();
        using MemoryStream stream = new();
        bitmap.Save(stream, ImageFormat.Png);

        byte[] bytes = stream.ToArray();
        icons[id] = bytes;
        Directory.CreateDirectory(iconFolder);
        File.WriteAllBytes(saved, bytes);
        return bytes;
    }
}
