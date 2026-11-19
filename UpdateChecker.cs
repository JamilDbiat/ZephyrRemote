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

    public string Stage { get; private set; } = "";
    public int Percent { get; private set; }

    public async Task<bool> Install()
    {
        if (!Available || !Installable || Stage == "downloading" || Stage == "installing")
            return false;

        Stage = "downloading";
        Percent = 0;
        string path = Path.Combine(Path.GetTempPath(), "ZephyrRemoteSetup.exe");
        try
        {
            using HttpClient http = Client();
            using HttpResponseMessage response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            long total = response.Content.Headers.ContentLength ?? 0;
            await using Stream source = await response.Content.ReadAsStreamAsync();
            await using (FileStream file = File.Create(path))
            {
                byte[] buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    if (total > 0)
                        Percent = (int)(done * 100 / total);
                }
            }
        }
        catch (HttpRequestException)
        {
            Stage = "failed";
            return false;
        }

        Stage = "installing";
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
