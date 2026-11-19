using System.Globalization;
using System.Net;
using System.Text;

namespace ZephyrRemote;

public class AgentServer
{
    private readonly string key;
    private readonly ScreenCapture screen = new();
    private readonly MouseController mouse = new();
    private readonly KeyboardController keyboard = new();

    public AgentServer(string key)
    {
        this.key = key;
    }

    public void Run()
    {
        HttpListener listener = new();
        listener.Prefixes.Add($"http://127.0.0.1:{AgentKey.Port}/");
        listener.Start();

        while (true)
        {
            HttpListenerContext context = listener.GetContext();
            try
            {
                NativeDesktop.OnInputDesktop(() => Handle(context));
            }
            catch (Exception)
            {
                context.Response.StatusCode = 500;
            }
            context.Response.Close();
        }
    }

    private void Handle(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        if (request.Headers["X-Key"] != key)
        {
            response.StatusCode = 403;
            return;
        }

        var query = request.QueryString;
        int display = Number(query["display"], 0);
        double x = Decimal(query["x"]);
        double y = Decimal(query["y"]);
        string button = query["button"] ?? "left";

        switch (request.Url!.AbsolutePath)
        {
            case "/state":
                Write(response, "text/plain", Encoding.UTF8.GetBytes(NativeDesktop.InputDesktopName()));
                break;
            case "/frame":
                Write(response, "image/jpeg", screen.Capture(display, Number(query["w"], 1280), Number(query["q"], 50)));
                break;
            case "/move":
                mouse.MoveTo(display, x, y);
                break;
            case "/down":
                mouse.MoveTo(display, x, y);
                Thread.Sleep(30);
                mouse.Press(button, true);
                break;
            case "/up":
                mouse.Press(button, false);
                break;
            case "/click":
                mouse.Click(display, x, y, button);
                break;
            case "/scroll":
                mouse.Scroll(Number(query["amount"], 0));
                break;
            case "/wake":
                mouse.Wake();
                break;
            case "/key":
                keyboard.Press(query["key"] ?? "");
                break;
            case "/type":
                using (StreamReader reader = new(request.InputStream, Encoding.UTF8))
                    keyboard.Type(reader.ReadToEnd());
                break;
            default:
                response.StatusCode = 404;
                break;
        }
    }

    private static void Write(HttpListenerResponse response, string type, byte[] data)
    {
        response.ContentType = type;
        response.ContentLength64 = data.Length;
        response.OutputStream.Write(data);
    }

    private static int Number(string? value, int fallback)
    {
        return int.TryParse(value, out int n) ? n : fallback;
    }

    private static double Decimal(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : -1;
    }
}
