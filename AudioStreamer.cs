using System.Net.WebSockets;
using System.Threading.Channels;
using NAudio.Wave;

namespace ZephyrRemote;

public class AudioStreamer
{
    private const int OutputRate = 24000;
    private const int ChunkBytes = 960 * 4;

    private readonly object sync = new();
    private readonly List<Channel<byte[]>> listeners = new();
    private readonly List<byte> pending = new();
    private WasapiLoopbackCapture? capture;
    private double position;

    public async Task Serve(WebSocket socket, CancellationToken token)
    {
        Channel<byte[]> channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(25) { FullMode = BoundedChannelFullMode.DropOldest });
        Add(channel);
        try
        {
            await foreach (byte[] chunk in channel.Reader.ReadAllAsync(token))
                await socket.SendAsync(chunk, WebSocketMessageType.Binary, true, token);
        }
        catch (Exception)
        {
        }
        finally
        {
            Remove(channel);
        }
    }

    private void Add(Channel<byte[]> channel)
    {
        lock (sync)
        {
            listeners.Add(channel);
            if (capture != null)
                return;

            position = 0;
            pending.Clear();
            capture = new WasapiLoopbackCapture();
            capture.DataAvailable += OnData;
            capture.StartRecording();
        }
    }

    private void Remove(Channel<byte[]> channel)
    {
        lock (sync)
        {
            listeners.Remove(channel);
            if (listeners.Count > 0 || capture == null)
                return;

            capture.StopRecording();
            capture.Dispose();
            capture = null;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        WaveFormat format = capture!.WaveFormat;
        int channels = format.Channels;
        int frames = e.BytesRecorded / (4 * channels);
        double step = format.SampleRate / (double)OutputRate;

        lock (sync)
        {
            while (position < frames)
            {
                int index = (int)position;
                float left = BitConverter.ToSingle(e.Buffer, index * channels * 4);
                float right = channels > 1 ? BitConverter.ToSingle(e.Buffer, (index * channels + 1) * 4) : left;
                AddSample(left);
                AddSample(right);
                position += step;
            }
            position -= frames;

            while (pending.Count >= ChunkBytes)
            {
                byte[] chunk = pending.GetRange(0, ChunkBytes).ToArray();
                pending.RemoveRange(0, ChunkBytes);
                foreach (Channel<byte[]> listener in listeners)
                    listener.Writer.TryWrite(chunk);
            }
        }
    }

    private void AddSample(float value)
    {
        short sample = (short)(Math.Clamp(value, -1f, 1f) * 32767);
        pending.Add((byte)sample);
        pending.Add((byte)(sample >> 8));
    }
}
