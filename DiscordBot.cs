using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ZephyrRemote;

public class DiscordBot
{
    private const string Api = "https://discord.com/api/v10";
    private const int Intents = 512 | 32768;

    private readonly Func<string> getUrl;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private CancellationTokenSource? cts;
    private string token = "";
    private string ownerId = "";
    private string guildId = "";

    public string Status { get; private set; } = "off";

    public DiscordBot(Func<string> getUrl)
    {
        this.getUrl = getUrl;
    }

    public void Start(string token, string ownerId, string guildId)
    {
        Stop();
        this.token = token;
        this.ownerId = ownerId;
        this.guildId = guildId;
        cts = new CancellationTokenSource();
        CancellationToken ct = cts.Token;
        Status = "connecting";
        Task.Run(() => Loop(ct));
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;
        Status = "off";
    }

    public static async Task<string> Verify(string token, string ownerId, string guildId)
    {
        using HttpClient http = Client(token);

        if (!(await http.GetAsync($"{Api}/users/@me")).IsSuccessStatusCode)
            return "Discord rejected the bot token.";
        if (!(await http.GetAsync($"{Api}/guilds/{guildId}")).IsSuccessStatusCode)
            return "Your bot isn't in that server, or the server ID is wrong.";
        if (!await DirectMessage(http, ownerId, "✅ **Zephyr Remote** is connected. Type `!zephyr` in your server and I'll DM you the link."))
            return "The bot couldn't DM you. Check your user ID, and allow DMs from server members in that server's privacy settings.";
        return "ok";
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int code = await RunOnce(ct);
            if (code == 4004)
            {
                Status = "Discord rejected the bot token.";
                return;
            }
            if (code == 4014)
            {
                Status = "Turn on MESSAGE CONTENT INTENT for your bot in the Discord Developer Portal, then save the bot again.";
                return;
            }
            if (ct.IsCancellationRequested)
                return;

            Status = "reconnecting";
            await Task.Delay(5000);
        }
    }

    private async Task<int> RunOnce(CancellationToken ct)
    {
        using ClientWebSocket ws = new();
        using CancellationTokenSource beat = CancellationTokenSource.CreateLinkedTokenSource(ct);
        int? seq = null;

        try
        {
            await ws.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), ct);

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                string? text = await Receive(ws, ct);
                if (text == null)
                    break;

                using JsonDocument doc = JsonDocument.Parse(text);
                JsonElement root = doc.RootElement;
                int op = root.GetProperty("op").GetInt32();
                if (root.TryGetProperty("s", out JsonElement s) && s.ValueKind == JsonValueKind.Number)
                    seq = s.GetInt32();

                if (op == 10)
                {
                    int interval = root.GetProperty("d").GetProperty("heartbeat_interval").GetInt32();
                    _ = Heartbeat(ws, interval, () => seq, beat.Token);
                    var identify = new
                    {
                        op = 2,
                        d = new
                        {
                            token,
                            intents = Intents,
                            properties = new Dictionary<string, string> { ["os"] = "windows", ["browser"] = "zephyr", ["device"] = "zephyr" }
                        }
                    };
                    await Send(ws, identify, ct);
                }
                else if (op == 0)
                {
                    string type = root.GetProperty("t").GetString() ?? "";
                    if (type == "READY")
                        Status = "on";
                    if (type == "MESSAGE_CREATE")
                        await OnMessage(root.GetProperty("d"));
                }
                else if (op == 7 || op == 9)
                {
                    break;
                }
            }
        }
        catch (Exception)
        {
        }

        beat.Cancel();
        return (int?)ws.CloseStatus ?? 0;
    }

    private async Task OnMessage(JsonElement message)
    {
        string content = message.TryGetProperty("content", out JsonElement c) ? (c.GetString() ?? "").Trim().ToLower() : "";
        if (content != "!zephyr")
            return;

        string author = message.GetProperty("author").GetProperty("id").GetString() ?? "";
        string guild = message.TryGetProperty("guild_id", out JsonElement g) ? g.GetString() ?? "" : "";
        if (author != ownerId || guild != guildId)
            return;

        string url = getUrl();
        string reply = url == ""
            ? "Zephyr Remote is running, but remote access is off or still connecting. Try again in a few seconds."
            : $"🖥️ Your **Zephyr Remote** link: {url}";

        using HttpClient http = Client(token);
        await DirectMessage(http, ownerId, reply);
    }

    private static async Task<bool> DirectMessage(HttpClient http, string userId, string text)
    {
        string body = JsonSerializer.Serialize(new { recipient_id = userId });
        HttpResponseMessage dm = await http.PostAsync($"{Api}/users/@me/channels", new StringContent(body, Encoding.UTF8, "application/json"));
        if (!dm.IsSuccessStatusCode)
            return false;

        using JsonDocument doc = JsonDocument.Parse(await dm.Content.ReadAsStringAsync());
        string channel = doc.RootElement.GetProperty("id").GetString() ?? "";
        string message = JsonSerializer.Serialize(new { content = text });
        HttpResponseMessage sent = await http.PostAsync($"{Api}/channels/{channel}/messages", new StringContent(message, Encoding.UTF8, "application/json"));
        return sent.IsSuccessStatusCode;
    }

    private async Task Heartbeat(ClientWebSocket ws, int interval, Func<int?> seq, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            await Task.Delay(interval, ct).ContinueWith(_ => { });
            if (ct.IsCancellationRequested)
                return;
            await Send(ws, new { op = 1, d = seq() }, ct);
        }
    }

    private async Task Send(ClientWebSocket ws, object payload, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await sendLock.WaitAsync(ct);
        try
        {
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private static async Task<string?> Receive(ClientWebSocket ws, CancellationToken ct)
    {
        byte[] buffer = new byte[8192];
        using MemoryStream stream = new();
        while (true)
        {
            WebSocketReceiveResult result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static HttpClient Client(string token)
    {
        HttpClient http = new();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);
        return http;
    }
}
