using System.Diagnostics;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Threading.Channels;

namespace ZephyrRemote;

public class VideoStreamer
{
    private const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    private static readonly string[] Encoders =
    {
        "-f lavfi -i \"ddagrab=output_idx={0}:framerate=60\" -c:v h264_nvenc -preset p1 -tune ull -zerolatency 1 -rc cbr -b:v {1} -g 120 -bf 0",
        "-f lavfi -i \"ddagrab=output_idx={0}:framerate=60,hwmap=derive_device=qsv,format=qsv\" -c:v h264_qsv -preset veryfast -low_power 1 -b:v {1} -g 120 -bf 0",
        "-f lavfi -i \"ddagrab=output_idx={0}:framerate=60\" -c:v h264_amf -usage ultralowlatency -quality speed -rc cbr -b:v {1} -g 120 -bf 0",
        "-f lavfi -i \"ddagrab=output_idx={0}:framerate=30,hwdownload,format=bgra,scale=1280:-2,format=yuv420p\" -c:v libx264 -preset ultrafast -tune zerolatency -b:v 1500k -maxrate 2M -bufsize 1M -g 60 -bf 0"
    };

    private readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZephyrRemote");
    private string? ffmpeg;
    private int encoder = -1;

    public bool Supported => FindFfmpeg() != null;

    public async Task Serve(WebSocket socket, int display, string bitrate, CancellationToken token)
    {
        string? exe = await EnsureFfmpeg();
        if (exe == null)
            return;

        Process? process = await StartEncoder(exe, display, bitrate);
        if (process == null)
            return;

        Channel<byte[]> frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(30) { FullMode = BoundedChannelFullMode.Wait });
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task reader = Task.Run(() => ReadFrames(process.StandardOutput.BaseStream, frames.Writer, stop.Token));

        try
        {
            await foreach (byte[] frame in frames.Reader.ReadAllAsync(stop.Token))
                await socket.SendAsync(frame, WebSocketMessageType.Binary, true, stop.Token);
        }
        catch (Exception)
        {
        }
        finally
        {
            stop.Cancel();
            if (!process.HasExited)
                process.Kill();
            await reader.ContinueWith(_ => { });
        }
    }

    private static async Task ReadFrames(Stream output, ChannelWriter<byte[]> writer, CancellationToken token)
    {
        byte[] buffer = new byte[1 << 16];
        List<byte> data = new();
        List<byte> unit = new();
        bool unitHasSlice = false;
        bool unitIsKey = false;
        bool dropping = false;
        int scanFrom = 0;
        int nalStart = -1;

        void Emit()
        {
            if (unit.Count > 0 && unitHasSlice)
            {
                if (dropping && unitIsKey)
                    dropping = false;
                if (!dropping)
                {
                    byte[] frame = new byte[unit.Count + 1];
                    frame[0] = (byte)(unitIsKey ? 1 : 0);
                    unit.CopyTo(frame, 1);
                    if (!writer.TryWrite(frame))
                        dropping = true;
                }
            }
            unit.Clear();
            unitHasSlice = false;
            unitIsKey = false;
        }

        void AddNal(int start, int end)
        {
            int type = data[start] & 0x1F;
            bool slice = type == 1 || type == 5;
            if (unitHasSlice)
                Emit();
            unit.AddRange(new byte[] { 0, 0, 0, 1 });
            unit.AddRange(data.GetRange(start, end - start));
            if (slice)
                unitHasSlice = true;
            if (type == 5)
                unitIsKey = true;
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                int read = await output.ReadAsync(buffer, token);
                if (read == 0)
                    break;
                data.AddRange(new ArraySegment<byte>(buffer, 0, read));

                for (int i = Math.Max(scanFrom, 0); i + 2 < data.Count; i++)
                {
                    if (data[i] != 0 || data[i + 1] != 0 || data[i + 2] != 1)
                        continue;

                    int codeStart = i > 0 && data[i - 1] == 0 ? i - 1 : i;
                    if (nalStart >= 0)
                        AddNal(nalStart, codeStart);
                    nalStart = i + 3;
                    i += 2;
                }

                if (nalStart > 0)
                {
                    data.RemoveRange(0, nalStart);
                    nalStart = 0;
                }
                scanFrom = Math.Max(0, data.Count - 3);
            }
        }
        catch (Exception)
        {
        }
        writer.TryComplete();
    }

    private async Task<Process?> StartEncoder(string exe, int display, string bitrate)
    {
        int first = encoder >= 0 ? encoder : 0;
        for (int i = first; i < Encoders.Length; i++)
        {
            string input = string.Format(Encoders[i], display, bitrate);
            ProcessStartInfo info = new(exe, $"-hide_banner -loglevel error {input} -f h264 -")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process process = Process.Start(info)!;
            _ = process.StandardError.ReadToEndAsync();
            await Task.Delay(encoder == i ? 150 : 1500);
            if (!process.HasExited)
            {
                encoder = i;
                return process;
            }
            if (encoder >= 0)
                encoder = -1;
        }
        return null;
    }

    private string? FindFfmpeg()
    {
        if (ffmpeg != null && File.Exists(ffmpeg))
            return ffmpeg;

        string local = Path.Combine(folder, "ffmpeg.exe");
        if (File.Exists(local))
            return ffmpeg = local;

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
            if (dir.Trim() != "" && File.Exists(candidate))
                return ffmpeg = candidate;
        }
        return null;
    }

    private async Task<string?> EnsureFfmpeg()
    {
        string? found = FindFfmpeg();
        if (found != null)
            return found;

        Directory.CreateDirectory(folder);
        string zip = Path.Combine(folder, "ffmpeg.zip");
        using (HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) })
            await File.WriteAllBytesAsync(zip, await http.GetByteArrayAsync(DownloadUrl));

        using (ZipArchive archive = ZipFile.OpenRead(zip))
        {
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
            entry?.ExtractToFile(Path.Combine(folder, "ffmpeg.exe"), true);
        }
        File.Delete(zip);
        return FindFfmpeg();
    }
}
