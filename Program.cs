using System.Text;
using System.Net.WebSockets;
using System.Globalization;
using System.Text.RegularExpressions;
using System.ServiceProcess;
using QRCoder;
using ZephyrRemote;

const int Port = 5050;

Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

if (args.Length > 0 && args[0] == "--service")
{
    ServiceBase.Run(new HelperService());
    return;
}

if (args.Length > 1 && args[0] == "--agent")
{
    new AgentServer(args[1]).Run();
    return;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");
builder.Logging.ClearProviders();

WebApplication app = builder.Build();

AppScanner scanner = new();
PowerController power = new();
NetworkInfo network = new();
ScreenCapture screen = new();
PinStore pins = new();
RemoteAccess remote = new(Port);
bool firstRun = !pins.HasPin;
ProcessManager processes = new();
MouseController mouse = new();
KeyboardController keyboard = new();
AgentClient agent = new();
AudioStreamer audio = new();
VideoStreamer video = new();
DeviceTracker devices = new();
FileBrowser files = new();
UpdateChecker updates = new();
TrayApp? trayApp = null;

string ClientIp(HttpContext context)
{
    string cf = context.Request.Headers["Cf-Connecting-IP"].ToString();
    return cf != "" ? cf : context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "?";
}

bool IsRemote(HttpContext context) => context.Request.Headers.ContainsKey("Cf-Connecting-IP");

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache"
});

app.UseWebSockets();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        if (pins.Locked)
        {
            context.Response.StatusCode = 423;
            return;
        }

        int triesBefore = pins.TriesLeft;
        if (!pins.Check(context.Request.Query["pin"].ToString()))
        {
            string where = IsRemote(context) ? "remote" : "home Wi-Fi";
            string who = $"{DeviceTracker.Describe(context.Request.Headers.UserAgent.ToString())} · {where} · IP {ClientIp(context)}";
            if (pins.Locked)
                remote.Alert($"🔒 **Zephyr locked** after {PinStore.MaxTries} wrong PINs.\nLast try: {who}\nUnlock with your recovery code or from the PC tray icon.");
            else if (pins.TriesLeft < triesBefore)
                remote.Alert($"⚠️ **Wrong PIN** entered on Zephyr ({pins.TriesLeft} tries left).\n{who}");
            context.Response.StatusCode = pins.Locked ? 423 : 401;
            return;
        }

        DeviceInfo? device = devices.Seen(context.Request.Query["dev"].ToString(), context.Request.Headers.UserAgent.ToString(), ClientIp(context), IsRemote(context), out bool fresh, out bool askApproval);
        if (device == null)
        {
            context.Response.StatusCode = 400;
            return;
        }
        if (askApproval)
            remote.Alert($"🛡️ **New device wants into Zephyr** (PIN was correct)\n{device.Name} · remote · IP {device.Ip}\nCode: **{device.Code}**\nTo let it in, reply `approve {device.Code}` to me, or tap Approve in ⚙️ Devices on your phone, or use the Zephyr tray icon on your PC.\nIf this isn't you, change your PIN.");
        else if (fresh && device.Trusted)
            remote.Alert($"🔔 **New sign-in to Zephyr**\n{device.Name} · {(device.Remote ? "remote" : "home Wi-Fi")} · IP {device.Ip}");
        if (!device.Trusted)
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { pending = true, code = device.Code });
            return;
        }
    }
    await next();
});

app.MapGet("/setup/status", () => new { hasPin = pins.HasPin, locked = pins.Locked, triesLeft = pins.TriesLeft });

app.MapPost("/setup/pin", async (HttpRequest request) =>
{
    string pin = (await new StreamReader(request.Body).ReadToEndAsync()).Trim();
    string? code = pins.Create(pin);
    return code == null ? Results.BadRequest() : Results.Ok(new { code });
});

app.MapPost("/setup/unlock", async (HttpContext context) =>
{
    string code = await new StreamReader(context.Request.Body).ReadToEndAsync();
    if (!pins.Unlock(code))
        return Results.BadRequest();
    remote.Alert($"🔓 **Zephyr unlocked** with the recovery code.\n{DeviceTracker.Describe(context.Request.Headers.UserAgent.ToString())} · IP {ClientIp(context)}");
    return Results.Ok();
});

app.MapGet("/api/devices", () => devices.All().Select(d => new
{
    id = d.Id,
    name = d.Name,
    remote = d.Remote,
    ip = d.Ip,
    lastSeen = d.LastSeen,
    firstSeen = d.FirstSeen,
    trusted = d.Trusted,
    code = d.Trusted ? "" : d.Code
}));

app.MapGet("/api/files/list", (string? path) =>
{
    if (string.IsNullOrEmpty(path))
        return Results.Ok(new { path = "", parent = (string?)null, items = files.Places() });
    List<FileEntry>? items = files.List(path);
    if (items == null)
        return Results.NotFound();
    return Results.Ok(new { path, parent = Path.GetDirectoryName(path), items });
});

app.MapGet("/api/files/download", (string path) =>
{
    if (!File.Exists(path))
        return Results.NotFound();
    return Results.File(path, "application/octet-stream", Path.GetFileName(path), enableRangeProcessing: true);
});

app.MapPost("/api/files/upload", async (HttpRequest request, string name, long offset) =>
{
    string? path = offset == 0 ? files.NewUploadPath(name) : files.UploadPath(name);
    if (path == null)
        return Results.NotFound();
    try
    {
        await using FileStream stream = new(path, FileMode.Append, FileAccess.Write);
        await request.Body.CopyToAsync(stream);
    }
    catch (Exception)
    {
        if (offset == 0)
            File.Delete(path);
        return Results.StatusCode(500);
    }
    return Results.Ok(new { name = Path.GetFileName(path) });
});

app.MapPost("/api/files/open", (string path) =>
{
    if (!File.Exists(path) && !Directory.Exists(path))
        return Results.NotFound();
    System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\"");
    return Results.Ok();
});

app.MapGet("/api/clipboard", () => Results.Text(ClipboardHelper.GetText()));

app.MapPost("/api/clipboard", async (HttpRequest request) =>
{
    ClipboardHelper.SetText(await new StreamReader(request.Body).ReadToEndAsync());
    return Results.Ok();
});

app.MapGet("/api/update", () => new
{
    current = updates.Current.ToString(),
    latest = updates.Latest?.ToString(),
    available = updates.Available,
    installable = updates.Installable
});

app.MapPost("/api/update/check", async () =>
{
    try
    {
        await updates.Check();
    }
    catch (HttpRequestException)
    {
        return Results.StatusCode(502);
    }
    return Results.Ok(new { current = updates.Current.ToString(), latest = updates.Latest?.ToString(), available = updates.Available, installable = updates.Installable });
});

app.MapPost("/api/update/install", () =>
{
    if (!updates.Available || !updates.Installable)
        return Results.BadRequest();
    remote.Alert($"⬆️ **Updating Zephyr** to v{updates.Latest}. It'll be back in about a minute.");
    _ = updates.Install();
    return Results.Ok(new { target = updates.Latest?.ToString() });
});

app.MapGet("/api/update/progress", () => new { stage = updates.Stage, percent = updates.Percent, current = updates.Current.ToString() });

_ = Task.Run(async () =>
{
    if (updates.JustUpdated())
        remote.Alert($"⬆️ **Zephyr updated** to v{updates.Current}.");

    bool startup = true;
    Version? announced = null;
    while (true)
    {
        await Task.Delay(startup ? TimeSpan.FromSeconds(45) : TimeSpan.FromHours(1));
        try
        {
            if (await updates.Check())
            {
                if (startup && updates.Installable)
                {
                    remote.Alert($"⬆️ **Updating Zephyr** to v{updates.Latest}. It'll be back in about a minute.");
                    await updates.Install();
                }
                else if (announced != updates.Latest)
                {
                    announced = updates.Latest;
                    trayApp?.NotifyUpdate();
                    remote.Alert($"⬆️ **Zephyr v{updates.Latest} is available.** Open ⚙️ in Zephyr and tap Update.");
                }
            }
        }
        catch (HttpRequestException)
        {
        }
        startup = false;
    }
});

app.MapPost("/api/devices/approve", (string id) =>
{
    DeviceInfo? device = devices.Approve(id);
    if (device == null)
        return Results.NotFound();
    remote.Alert($"✅ **Device approved:** {device.Name}");
    return Results.Ok();
});

app.MapPost("/api/devices/remove", (string id) =>
{
    DeviceInfo? device = devices.Remove(id);
    if (device == null)
        return Results.NotFound();
    remote.Alert($"🗑️ **Device removed from Zephyr:** {device.Name}");
    return Results.Ok();
});

_ = Task.Run(async () =>
{
    Regex approveCommand = new(@"🔑 approve (\d{6})");
    while (true)
    {
        await Task.Delay(4000);
        if (!devices.HasPending || !remote.HasBot)
            continue;
        try
        {
            foreach (string text in await remote.RecentBotMessages())
            {
                Match match = approveCommand.Match(text);
                DeviceInfo? device = match.Success ? devices.ApproveCode(match.Groups[1].Value) : null;
                if (device != null)
                    remote.Alert($"✅ **Device approved:** {device.Name}");
            }
        }
        catch (HttpRequestException)
        {
        }
    }
});

app.MapPost("/api/pin/change", async (HttpRequest request) =>
{
    string pin = (await new StreamReader(request.Body).ReadToEndAsync()).Trim();
    return pins.Change(pin) ? Results.Ok() : Results.BadRequest();
});

app.MapGet("/api/recovery", () => new { code = pins.RecoveryCode() });

app.MapGet("/qr.png", (bool? remoteLink) =>
{
    string link = remoteLink == true && remote.Url != "" ? remote.Url : $"http://{network.LocalIp}:{Port}";
    using QRCodeGenerator generator = new();
    using QRCodeData data = generator.CreateQrCode(link, QRCodeGenerator.ECCLevel.Q);
    return Results.File(new PngByteQRCode(data).GetGraphic(12), "image/png");
});

app.MapGet("/setup/url", () => new { url = $"http://{network.LocalIp}:{Port}", remote = remote.Url });

app.MapGet("/api/remote", () => new
{
    enabled = remote.Settings.Enabled,
    status = remote.Status,
    url = remote.Url,
    webhook = remote.Settings.DiscordWebhook != "",
    bot = remote.HasBot,
    botStatus = remote.BotStatus
});

app.MapPost("/api/remote/bot", async (HttpRequest request) =>
{
    var body = await request.ReadFromJsonAsync<Dictionary<string, string>>() ?? new();
    string result = await remote.SetBot(body.GetValueOrDefault("token", ""), body.GetValueOrDefault("channel", ""));
    return result == "ok" ? Results.Ok() : Results.BadRequest(result);
});

app.MapPost("/api/remote/on", () =>
{
    remote.Enable();
    return Results.Ok();
});

app.MapPost("/api/remote/off", () =>
{
    remote.Disable();
    return Results.Ok();
});

app.MapPost("/api/remote/webhook", async (HttpRequest request) =>
{
    string url = await new StreamReader(request.Body).ReadToEndAsync();
    return remote.SetWebhook(url) ? Results.Ok() : Results.BadRequest();
});

app.MapGet("/api/apps", () => scanner.GetApps());

app.MapGet("/api/icon/{id}", (string id, HttpResponse response) =>
{
    byte[] icon = scanner.GetIcon(id);
    response.Headers.CacheControl = "private, max-age=604800";
    return icon.Length == 0 ? Results.NotFound() : Results.File(icon, "image/png");
});

app.MapPost("/api/launch/{id}", (string id) => scanner.Launch(id) ? Results.Ok() : Results.NotFound());

app.MapPost("/api/power/{action}", (string action) => power.Run(action) ? Results.Ok() : Results.BadRequest());

app.MapGet("/api/running", () => processes.GetRunning());

app.MapPost("/api/close/{id}", (string id) => processes.Close(id) ? Results.Ok() : Results.NotFound());

app.MapGet("/api/running/icon/{id}", (string id) =>
{
    byte[] icon = processes.GetIcon(id);
    return icon.Length == 0 ? Results.NotFound() : Results.File(icon, "image/png");
});

app.MapGet("/api/screens", () => new { count = screen.Count });

app.MapGet("/api/screen/{index:int}", (int index, HttpResponse response) =>
{
    byte[] image = agent.Available ? agent.Frame(index, 1600, 60L).GetAwaiter().GetResult() : screen.Capture(index, 1600, 60L);
    response.Headers.CacheControl = "no-store";
    return image.Length == 0 ? Results.NotFound() : Results.File(image, "image/jpeg");
});

app.MapGet("/api/stream/{index:int}", async (int index, HttpContext context) =>
{
    context.Response.ContentType = "multipart/x-mixed-replace; boundary=frame";
    context.Response.Headers.CacheControl = "no-store";
    CancellationToken token = context.RequestAborted;

    while (!token.IsCancellationRequested)
    {
        long start = Environment.TickCount64;
        byte[] image = agent.Available ? await agent.Frame(index, 1280, 50L) : screen.Capture(index, 1280, 50L);
        if (image.Length == 0)
            break;

        await context.Response.WriteAsync($"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: {image.Length}\r\n\r\n", token);
        await context.Response.Body.WriteAsync(image, token);
        await context.Response.WriteAsync("\r\n", token);
        await context.Response.Body.FlushAsync(token);

        int wait = 33 - (int)(Environment.TickCount64 - start);
        if (wait > 0)
            await Task.Delay(wait, token);
    }
});

async Task HandleInput(string[] p)
{
    CultureInfo c = CultureInfo.InvariantCulture;
    int display = p.Length > 1 && int.TryParse(p[1], out int d) ? d : 0;
    double x = p.Length > 2 && double.TryParse(p[2], NumberStyles.Float, c, out double px) ? px : 0;
    double y = p.Length > 3 && double.TryParse(p[3], NumberStyles.Float, c, out double py) ? py : 0;
    string button = p.Length > 4 && (p[4] == "left" || p[4] == "right") ? p[4] : "left";

    switch (p[0])
    {
        case "move":
            if (agent.Available)
                await agent.Post($"/move?{AgentClient.Point(display, x, y)}");
            else
                mouse.MoveTo(display, x, y);
            break;
        case "click":
            if (agent.Available)
                await agent.Post($"/click?{AgentClient.Point(display, x, y)}&button={button}");
            else
                mouse.Click(display, x, y, button);
            break;
        case "down":
            if (agent.Available)
            {
                await agent.Post($"/down?{AgentClient.Point(display, x, y)}&button={button}");
                break;
            }
            mouse.MoveTo(display, x, y);
            await Task.Delay(30);
            mouse.Press(button, true);
            break;
        case "up":
            if (agent.Available)
                await agent.Post($"/up?button={button}");
            else
                mouse.Press(button, false);
            break;
        case "scroll":
            if (agent.Available)
                await agent.Post($"/scroll?amount={display}");
            else
                mouse.Scroll(display);
            break;
    }
}

app.Map("/api/input", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
    byte[] buffer = new byte[4096];
    while (socket.State == WebSocketState.Open)
    {
        WebSocketReceiveResult result;
        try
        {
            result = await socket.ReceiveAsync(buffer, context.RequestAborted);
        }
        catch (Exception)
        {
            break;
        }
        if (result.MessageType == WebSocketMessageType.Close)
            break;
        try
        {
            await HandleInput(Encoding.UTF8.GetString(buffer, 0, result.Count).Split(' '));
        }
        catch (Exception)
        {
        }
    }
});

app.MapPost("/api/mouse/click", async (int display, double x, double y, string button) =>
{
    if (agent.Available)
        await agent.Post($"/click?{AgentClient.Point(display, x, y)}&button={button}");
    else if (!mouse.Click(display, x, y, button))
        return Results.BadRequest();
    return Results.Ok();
});

app.MapPost("/api/mouse/move", async (int display, double x, double y) =>
{
    if (agent.Available)
        await agent.Post($"/move?{AgentClient.Point(display, x, y)}");
    else if (!mouse.MoveTo(display, x, y))
        return Results.BadRequest();
    return Results.Ok();
});

app.MapPost("/api/mouse/down", async (int display, double x, double y, string button) =>
{
    if (agent.Available)
    {
        await agent.Post($"/down?{AgentClient.Point(display, x, y)}&button={button}");
        return Results.Ok();
    }
    if (!mouse.MoveTo(display, x, y))
        return Results.BadRequest();
    Thread.Sleep(30);
    mouse.Press(button, true);
    return Results.Ok();
});

app.MapPost("/api/mouse/up", async (string button) =>
{
    if (agent.Available)
        await agent.Post($"/up?button={button}");
    else
        mouse.Press(button, false);
    return Results.Ok();
});

app.MapPost("/api/mouse/scroll", async (int amount) =>
{
    if (agent.Available)
        await agent.Post($"/scroll?amount={amount}");
    else
        mouse.Scroll(amount);
    return Results.Ok();
});

app.MapPost("/api/wake", async () =>
{
    if (agent.Available)
        await agent.Post("/wake");
    else
        mouse.Wake();
    return Results.Ok();
});

app.MapGet("/api/lockstate", () => new { locked = NativeDesktop.InputDesktopName() != "Default", helper = agent.Available });

app.MapPost("/api/keys/type", async (HttpRequest request) =>
{
    string text = await new StreamReader(request.Body).ReadToEndAsync();
    if (agent.Available)
        await agent.Post("/type", text);
    else
        keyboard.Type(text);
    return Results.Ok();
});

app.MapPost("/api/keys/press", async (string key) =>
{
    if (agent.Available)
    {
        await agent.Post($"/key?key={Uri.EscapeDataString(key)}");
        return Results.Ok();
    }
    return keyboard.Press(key) ? Results.Ok() : Results.BadRequest();
});

app.Map("/api/audio", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }
    using System.Net.WebSockets.WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
    await audio.Serve(socket, context.RequestAborted);
});

app.Map("/api/video/{index:int}", async (HttpContext context, int index, string? quality) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }
    string bitrate = quality switch
    {
        "low" => "2M",
        "normal" => "4M",
        _ => "8M"
    };
    using System.Net.WebSockets.WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
    await video.Serve(socket, index, bitrate, context.RequestAborted);
});

app.MapGet("/api/info", () => new { mac = network.MacAddress, ip = network.LocalIp, name = Environment.MachineName, version = updates.Current.ToString() });

scanner.Refresh();
app.Start();
remote.Start();
Task.Run(ServiceInstaller.EnsureInstalled);
AppDomain.CurrentDomain.ProcessExit += (s, e) => remote.Shutdown();

Thread trayThread = new(() =>
{
    trayApp = new TrayApp(Port, firstRun, pins, devices, updates);
    Application.Run(trayApp);
});
trayThread.SetApartmentState(ApartmentState.STA);
trayThread.Start();
trayThread.Join();
