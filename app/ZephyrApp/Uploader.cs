using System.Net.Http.Headers;
using System.Text.Json;

namespace ZephyrApp;

public class Uploader
{
    private const int ChunkSize = 8 * 1024 * 1024;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly string baseUrl;
    private readonly string auth;
    private long sentBytes;

    public Uploader(string baseUrl, string auth)
    {
        this.baseUrl = baseUrl;
        this.auth = auth;
    }

    public int Done { get; private set; }
    public int Failed { get; private set; }
    public long SentBytes => Interlocked.Read(ref sentBytes);

    public async Task SendAll(List<PickedFile> files, int lanes)
    {
        Queue<PickedFile> queue = new(files);
        object sync = new();

        async Task Worker()
        {
            while (true)
            {
                PickedFile? file;
                lock (sync)
                    file = queue.Count > 0 ? queue.Dequeue() : null;
                if (file == null)
                    return;

                bool ok = await SendFile(file);
                lock (sync)
                {
                    if (ok)
                        Done++;
                    else
                        Failed++;
                }
                if (file.TempPath != "" && File.Exists(file.TempPath))
                    File.Delete(file.TempPath);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, Math.Min(lanes, files.Count)).Select(_ => Worker()));
    }

    private async Task<bool> SendFile(PickedFile file)
    {
        string name = file.Name;
        long offset = 0;
        int failures = 0;
        Stream? stream = null;
        byte[] buffer = new byte[ChunkSize];

        while (failures < 6)
        {
            try
            {
                if (stream == null)
                {
                    stream = file.Open();
                    await Skip(stream, offset, buffer);
                }

                int length = await Fill(stream, buffer);
                ByteArrayContent content = new(buffer, 0, length);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                string url = $"{baseUrl}/api/files/upload?name={Uri.EscapeDataString(name)}&offset={offset}&len={length}&{auth}";
                HttpResponseMessage res = await http.PostAsync(url, content);
                if (!res.IsSuccessStatusCode)
                    throw new HttpRequestException(res.StatusCode.ToString());

                using (JsonDocument doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync()))
                    name = doc.RootElement.GetProperty("name").GetString() ?? name;

                offset += length;
                Interlocked.Add(ref sentBytes, length);
                failures = 0;
                if (offset >= file.Size || length == 0)
                {
                    stream.Dispose();
                    return true;
                }
            }
            catch (Exception)
            {
                failures++;
                stream?.Dispose();
                stream = null;
                await Task.Delay(1500 * failures);
                if (offset > 0)
                {
                    long size = await ServerSize(name);
                    if (size >= 0)
                    {
                        Interlocked.Add(ref sentBytes, size - offset);
                        offset = size;
                    }
                }
            }
        }

        stream?.Dispose();
        Interlocked.Add(ref sentBytes, file.Size - offset);
        return false;
    }

    private async Task<long> ServerSize(string name)
    {
        try
        {
            string json = await http.GetStringAsync($"{baseUrl}/api/files/upload/size?name={Uri.EscapeDataString(name)}&{auth}");
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("size").GetInt64();
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static async Task<int> Fill(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private static async Task Skip(Stream stream, long count, byte[] buffer)
    {
        if (count == 0)
            return;
        if (stream.CanSeek)
        {
            stream.Seek(count, SeekOrigin.Begin);
            return;
        }
        while (count > 0)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)));
            if (read == 0)
                return;
            count -= read;
        }
    }
}
