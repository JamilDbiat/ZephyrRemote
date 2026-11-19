using System.Globalization;
using System.Text;

namespace ZephyrRemote;

public class AgentClient
{
    private readonly HttpClient http = new() { BaseAddress = new Uri($"http://127.0.0.1:{AgentKey.Port}"), Timeout = TimeSpan.FromSeconds(3) };
    private DateTime checkedAt = DateTime.MinValue;
    private bool available;
    private string key = "";

    public bool Available
    {
        get
        {
            if ((DateTime.UtcNow - checkedAt).TotalSeconds > 5)
            {
                checkedAt = DateTime.UtcNow;
                available = Ping();
            }
            return available;
        }
    }

    public async Task<byte[]> Frame(int display, int width, long quality)
    {
        HttpResponseMessage response = await Send(HttpMethod.Get, $"/frame?display={display}&w={width}&q={quality}", null);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : Array.Empty<byte>();
    }

    public Task Post(string path, string? body = null)
    {
        return Send(HttpMethod.Post, path, body);
    }

    public static string Point(int display, double x, double y)
    {
        return $"display={display}&x={x.ToString(CultureInfo.InvariantCulture)}&y={y.ToString(CultureInfo.InvariantCulture)}";
    }

    private bool Ping()
    {
        key = AgentKey.Read();
        if (key == "")
            return false;

        HttpResponseMessage? response = null;
        try
        {
            response = Send(HttpMethod.Get, "/state", null).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return false;
        }
        return response.IsSuccessStatusCode;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? body)
    {
        HttpRequestMessage request = new(method, path);
        request.Headers.Add("X-Key", key);
        if (body != null)
            request.Content = new StringContent(body, Encoding.UTF8, "text/plain");

        HttpResponseMessage response = await http.SendAsync(request);
        if ((int)response.StatusCode == 403)
            checkedAt = DateTime.MinValue;
        return response;
    }
}
