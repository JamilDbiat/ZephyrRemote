using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace ZephyrRemote;

public class UpdateChecker
{
    public const string Repo = "JamilDbiat/ZephyrRemote";

    private string downloadUrl = "";

    public Version Current { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0));
    public Version? Latest { get; private set; }
    public bool Available => Latest != null && Latest > Current && downloadUrl != "";
    public bool Installable => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public async Task<bool> Check()
    {
        using HttpClient http = Client();
        HttpResponseMessage response = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        if (!response.IsSuccessStatusCode)
            return false;

        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out Version? latest))
            return false;

        foreach (JsonElement asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            if ((asset.GetProperty("name").GetString() ?? "").EndsWith("Setup.exe"))
                downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
        }
        Latest = Normalize(latest);
        return Available;
    }

    public async Task<bool> Install()
    {
        if (!Available || !Installable)
            return false;

        string path = Path.Combine(Path.GetTempPath(), "ZephyrRemoteSetup.exe");
        using HttpClient http = Client();
        await using (FileStream file = File.Create(path))
            await (await http.GetStreamAsync(downloadUrl)).CopyToAsync(file);

        Process.Start(new ProcessStartInfo(path, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
        return true;
    }

    public bool JustUpdated()
    {
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZephyrRemote", "version.txt");
        string before = File.Exists(file) ? File.ReadAllText(file).Trim() : "";
        File.WriteAllText(file, Current.ToString());
        return before != "" && before != Current.ToString();
    }

    private static HttpClient Client()
    {
        HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ZephyrRemote");
        return http;
    }

    private static Version Normalize(Version v)
    {
        return new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
    }
}
