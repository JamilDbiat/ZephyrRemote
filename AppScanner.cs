using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;

namespace ZephyrRemote;

public class AppScanner
{
    private readonly Dictionary<string, AppEntry> apps = new();
    private readonly Dictionary<string, byte[]> icons = new();

    public List<AppEntry> GetApps()
    {
        apps.Clear();
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

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
                if (ext == ".lnk" && IsBroken(file))
                    continue;
                if (!names.Add(name))
                    continue;

                string id = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(file)))[..12];
                apps[id] = new AppEntry { Id = id, Name = name, Path = file };
            }
        }

        return apps.Values.OrderBy(a => a.Name).ToList();
    }

    private dynamic? shell;
    private readonly Dictionary<string, string> targets = new();

    private bool IsBroken(string shortcut)
    {
        if (!targets.TryGetValue(shortcut, out string? target))
        {
            target = TargetOf(shortcut);
            targets[shortcut] = target;
        }
        return target != "" && !File.Exists(target) && !Directory.Exists(target);
    }

    private string TargetOf(string shortcut)
    {
        if (shell == null)
        {
            Type? type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null)
                return "";
            shell = Activator.CreateInstance(type);
        }

        dynamic link = shell!.CreateShortcut(shortcut);
        string target = link.TargetPath;
        return target;
    }

    public bool Launch(string id)
    {
        if (!apps.ContainsKey(id))
            GetApps();
        if (!apps.TryGetValue(id, out AppEntry? app))
            return false;

        Process.Start("explorer.exe", "\"" + app.Path + "\"");
        return true;
    }

    public byte[] GetIcon(string id)
    {
        if (icons.TryGetValue(id, out byte[]? cached))
            return cached;
        if (!apps.ContainsKey(id))
            GetApps();
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
        return bytes;
    }
}
