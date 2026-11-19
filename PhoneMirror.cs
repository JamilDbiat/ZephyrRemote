using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZephyrRemote;

public class PhoneMirror
{
    private readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZephyrRemote");
    private string AndroidIpFile => Path.Combine(folder, "android-ip.txt");

    public bool HasAndroidWifi => File.Exists(AndroidIpFile);

    public async Task<string> ShowIphone(Action<string> status)
    {
        string? exe = Find("airplay", "uxplay-windows.exe");
        if (exe == null)
        {
            status("Downloading iPhone mirroring (first time only, about 110 MB)…");
            exe = await Download("leapbtw/uxplay-windows", "uxplay-windows.zip", "airplay", "uxplay-windows.exe");
            if (exe == null)
                return "Couldn't download iPhone mirroring. Check your internet and try again.";
        }

        await AllowThroughFirewall(Path.GetDirectoryName(exe)!);
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        return "";
    }

    public async Task<string> ShowAndroid(bool wifi, Action<string> status)
    {
        string? exe = await Scrcpy(status);
        if (exe == null)
            return "Couldn't download Android mirroring. Check your internet and try again.";

        string args = "--window-title \"Zephyr · Android\" --stay-awake";
        if (wifi)
        {
            if (!HasAndroidWifi)
                return "Set up Wi-Fi first: plug your phone in with a USB cable and choose \"Set up Android Wi-Fi\".";
            args = $"--tcpip={File.ReadAllText(AndroidIpFile).Trim()}:5555 " + args;
        }
        else
        {
            args = "--select-usb " + args;
        }

        return await Launch(exe, args);
    }

    public async Task<string> SetupAndroidWifi(Action<string> status)
    {
        string? exe = await Scrcpy(status);
        if (exe == null)
            return "Couldn't download Android mirroring. Check your internet and try again.";

        string adb = Path.Combine(Path.GetDirectoryName(exe)!, "adb.exe");
        string route = await Run(adb, "-d shell ip route");
        Match match = Regex.Match(route, @"src (\d+\.\d+\.\d+\.\d+)");
        if (!match.Success)
            return "No Android phone found on USB, or it isn't on Wi-Fi. Plug it in, turn on USB debugging, tap Allow on the phone, and connect it to Wi-Fi.";

        await Run(adb, "-d tcpip 5555");
        File.WriteAllText(AndroidIpFile, match.Groups[1].Value);
        await Task.Delay(2000);
        return await Launch(exe, $"--tcpip={match.Groups[1].Value}:5555 --window-title \"Zephyr · Android\" --stay-awake");
    }

    private static async Task AllowThroughFirewall(string dir)
    {
        string script = "Get-NetFirewallRule -DisplayName 'Zephyr Remote - iPhone mirroring*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule; "
            + $"foreach ($e in '{dir}\\uxplay-windows.exe', '{dir}\\mDNSResponder.exe') {{ "
            + "New-NetFirewallRule -DisplayName ('Zephyr Remote - iPhone mirroring ' + [IO.Path]::GetFileNameWithoutExtension($e)) -Direction Inbound -Program $e -Action Allow -Profile Any | Out-Null }";
        await Run("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"");
    }

    private async Task<string?> Scrcpy(Action<string> status)
    {
        string? exe = Find("scrcpy", "scrcpy.exe");
        if (exe != null)
            return exe;
        status("Downloading Android mirroring (first time only, about 10 MB)…");
        return await Download("Genymobile/scrcpy", "win64", "scrcpy", "scrcpy.exe");
    }

    private static async Task<string> Launch(string exe, string args)
    {
        ProcessStartInfo info = new(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(exe)
        };
        Process process = Process.Start(info)!;
        Task<string> errors = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();

        Task exited = process.WaitForExitAsync();
        if (await Task.WhenAny(exited, Task.Delay(8000)) != exited)
            return "";

        string text = await errors;
        if (text.Contains("Could not find any ADB device") || text.Contains("No device"))
            return "No Android phone found. Plug it in with a USB cable, turn on USB debugging, and tap Allow on the phone.";
        if (text.Contains("unauthorized"))
            return "Tap Allow on your phone (Allow USB debugging), then try again.";
        if (text.Contains("Could not connect") || text.Contains("failed to connect"))
            return "Couldn't reach your phone over Wi-Fi. Make sure it's on the same Wi-Fi, or plug it in and set up Wi-Fi again.";
        return process.ExitCode == 0 ? "" : "Android mirroring stopped: " + text.Trim().Split('\n').LastOrDefault();
    }

    private string? Find(string sub, string exeName)
    {
        string dir = Path.Combine(folder, sub);
        if (!Directory.Exists(dir))
            return null;
        return Directory.GetFiles(dir, exeName, SearchOption.AllDirectories).FirstOrDefault();
    }

    private async Task<string?> Download(string repo, string assetMatch, string sub, string exeName)
    {
        using HttpClient http = new() { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ZephyrRemote");

        string url = "";
        using (JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest")))
        {
            foreach (JsonElement asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (name.Contains(assetMatch) && name.EndsWith(".zip") && !name.Contains("arm64"))
                    url = asset.GetProperty("browser_download_url").GetString() ?? "";
            }
        }
        if (url == "")
            return null;

        string zip = Path.Combine(folder, sub + ".zip");
        await using (FileStream file = File.Create(zip))
            await (await http.GetStreamAsync(url)).CopyToAsync(file);

        string dir = Path.Combine(folder, sub);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        ZipFile.ExtractToDirectory(zip, dir);
        File.Delete(zip);
        return Find(sub, exeName);
    }

    private static async Task<string> Run(string exe, string args)
    {
        ProcessStartInfo info = new(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        Process process = Process.Start(info)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }
}
