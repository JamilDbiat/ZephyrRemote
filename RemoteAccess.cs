using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZephyrRemote;

public class RemoteAccess
{
    private const string DownloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";

    private readonly string exePath;
    private readonly string settingsFile;
    private readonly int port;
    private readonly object sync = new();
    private Process? tunnel;

    public RemoteSettings Settings { get; private set; } = new();
    public string Url { get; private set; } = "";
    public string Status { get; private set; } = "off";

    public RemoteAccess(int port)
    {
        this.port = port;
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZephyrRemote");
        Directory.CreateDirectory(folder);
        exePath = Path.Combine(folder, "cloudflared.exe");
        settingsFile = Path.Combine(folder, "remote.json");

        if (File.Exists(settingsFile))
            Settings = JsonSerializer.Deserialize<RemoteSettings>(File.ReadAllText(settingsFile)) ?? new RemoteSettings();

    }

    public void Start()
    {
        foreach (Process old in Process.GetProcessesByName("cloudflared"))
        {
            if (string.Equals(old.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase))
                old.Kill();
        }

        if (Settings.Enabled)
            Task.Run(Run);
    }

    public void Enable()
    {
        Settings.Enabled = true;
        Save();
        lock (sync)
        {
            if (tunnel == null || tunnel.HasExited)
                Task.Run(Run);
        }
    }

    public void Disable()
    {
        Settings.Enabled = false;
        Save();
        Shutdown();
        Url = "";
        Status = "off";
    }

    public bool SetWebhook(string url)
    {
        url = url.Trim();
        if (url != "" && !url.StartsWith("https://discord.com/api/webhooks/") && !url.StartsWith("https://discordapp.com/api/webhooks/"))
            return false;

        Settings.DiscordWebhook = url;
        Save();
        if (url != "" && Url != "")
            _ = Notify();
        return true;
    }

    public bool HasBot => Settings.BotTokenEncrypted != "" && Settings.ChannelId != "";
    public string BotStatus { get; private set; } = "";

    public async Task<string> SetBot(string token, string channelId)
    {
        token = token.Trim();
        channelId = new string(channelId.Where(char.IsDigit).ToArray());

        if (token == "" && channelId == "")
        {
            Settings.BotTokenEncrypted = "";
            Settings.ChannelId = "";
            Save();
            return "ok";
        }

        if (token.Length < 50)
            return "That bot token looks too short.";
        if (channelId.Length < 15)
            return "The channel ID should be a long number.";

        string text = Url == "" ? "✅ Zephyr Remote on your PC is connected." : $"🖥️ **Zephyr Remote** is online: {Url}";
        HttpResponseMessage response = await SendWithBot(token, channelId, text);
        if (!response.IsSuccessStatusCode)
            return $"Discord said {(int)response.StatusCode}. Check the token, the channel ID, and that the bot can send messages there.";

        byte[] secret = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        Settings.BotTokenEncrypted = Convert.ToBase64String(secret);
        Settings.ChannelId = channelId;
        Save();
        BotStatus = "ok";
        return "ok";
    }

    private static async Task<HttpResponseMessage> SendWithBot(string token, string channelId, string text)
    {
        using HttpClient http = new();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", token);
        string json = JsonSerializer.Serialize(new { content = text });
        return await http.PostAsync($"https://discord.com/api/v10/channels/{channelId}/messages", new StringContent(json, Encoding.UTF8, "application/json"));
    }

    public void Shutdown()
    {
        lock (sync)
        {
            if (tunnel != null && !tunnel.HasExited)
                tunnel.Kill();
            tunnel = null;
        }
    }

    private async Task Run()
    {
        Url = "";
        try
        {
            if (!File.Exists(exePath))
            {
                Status = "downloading";
                using HttpClient http = new();
                byte[] bytes = await http.GetByteArrayAsync(DownloadUrl);
                await File.WriteAllBytesAsync(exePath, bytes);
            }

            Status = "connecting";
            ProcessStartInfo info = new(exePath, $"tunnel --no-autoupdate --url http://localhost:{port}")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process = new() { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (s, e) => ReadLine(e.Data);
            process.ErrorDataReceived += (s, e) => ReadLine(e.Data);
            process.Exited += (s, e) => Restart();

            lock (sync)
            {
                process.Start();
                tunnel = process;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Status = "error: " + ex.Message;
        }
    }

    private void Restart()
    {
        if (!Settings.Enabled)
            return;

        Status = "reconnecting";
        Url = "";
        Task.Delay(5000).ContinueWith(_ => Run());
    }

    private void ReadLine(string? line)
    {
        Match match = Regex.Match(line ?? "", @"https://[a-z0-9-]+\.trycloudflare\.com");
        if (!match.Success || match.Value == Url)
            return;

        Url = match.Value;
        Status = "on";
        _ = Notify();
    }

    private string botId = "";

    public async Task<List<string>> RecentBotMessages()
    {
        List<string> result = new();
        if (!HasBot)
            return result;

        byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(Settings.BotTokenEncrypted), null, DataProtectionScope.CurrentUser);
        using HttpClient http = new();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", Encoding.UTF8.GetString(plain));

        if (botId == "")
        {
            using JsonDocument me = JsonDocument.Parse(await http.GetStringAsync("https://discord.com/api/v10/users/@me"));
            botId = me.RootElement.GetProperty("id").GetString() ?? "";
        }

        using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync($"https://discord.com/api/v10/channels/{Settings.ChannelId}/messages?limit=20"));
        foreach (JsonElement message in doc.RootElement.EnumerateArray())
        {
            if (message.GetProperty("author").GetProperty("id").GetString() == botId)
                result.Add(message.GetProperty("content").GetString() ?? "");
        }
        return result;
    }

    public void Alert(string text)
    {
        _ = SendAlert(text);
    }

    private async Task SendAlert(string text)
    {
        if (HasBot)
        {
            byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(Settings.BotTokenEncrypted), null, DataProtectionScope.CurrentUser);
            await SendWithBot(Encoding.UTF8.GetString(plain), Settings.ChannelId, text);
            return;
        }
        if (Settings.DiscordWebhook == "")
            return;

        string json = JsonSerializer.Serialize(new { content = text });
        using HttpClient http = new();
        await http.PostAsync(Settings.DiscordWebhook, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    private async Task Notify()
    {
        if (Url == "")
            return;

        string text = $"🖥️ **Zephyr Remote** is online: {Url}";

        if (HasBot)
        {
            byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(Settings.BotTokenEncrypted), null, DataProtectionScope.CurrentUser);
            HttpResponseMessage response = await SendWithBot(Encoding.UTF8.GetString(plain), Settings.ChannelId, text);
            BotStatus = response.IsSuccessStatusCode ? "ok" : $"Discord said {(int)response.StatusCode} when sending the link.";
            return;
        }

        if (Settings.DiscordWebhook == "")
            return;

        string json = JsonSerializer.Serialize(new { content = text });
        using HttpClient http = new();
        HttpResponseMessage hook = await http.PostAsync(Settings.DiscordWebhook, new StringContent(json, Encoding.UTF8, "application/json"));
        BotStatus = hook.IsSuccessStatusCode ? "" : $"Webhook not working (Discord said {(int)hook.StatusCode}).";
    }

    private void Save()
    {
        File.WriteAllText(settingsFile, JsonSerializer.Serialize(Settings));
    }
}
