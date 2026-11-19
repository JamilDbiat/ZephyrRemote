using System.Security.Cryptography;
using System.Text.Json;

namespace ZephyrRemote;

public class DeviceTracker
{
    private readonly string file;
    private readonly object sync = new();
    private readonly Dictionary<string, DeviceInfo> devices = new();
    private DateTime savedAt = DateTime.MinValue;

    public DeviceTracker()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZephyrRemote");
        Directory.CreateDirectory(folder);
        file = Path.Combine(folder, "devices.json");
        if (File.Exists(file))
        {
            foreach (DeviceInfo d in JsonSerializer.Deserialize<List<DeviceInfo>>(File.ReadAllText(file)) ?? new())
                devices[d.Id] = d;
        }

        if (devices.Count > 0 && !devices.Values.Any(d => d.Trusted))
        {
            foreach (DeviceInfo d in devices.Values)
                d.Trusted = true;
            Save();
        }
    }

    public bool HasPending
    {
        get
        {
            lock (sync)
                return devices.Values.Any(d => !d.Trusted);
        }
    }

    public DeviceInfo? Seen(string id, string userAgent, string ip, bool remote, out bool fresh, out bool askApproval)
    {
        fresh = false;
        askApproval = false;
        if (id.Length < 8 || id.Length > 64)
            return null;

        lock (sync)
        {
            DateTime now = DateTime.UtcNow;
            fresh = !devices.TryGetValue(id, out DeviceInfo? device) || now - device.LastSeen > TimeSpan.FromHours(12);
            device ??= new DeviceInfo { Id = id, FirstSeen = now };
            device.Name = Describe(userAgent);
            device.Ip = ip;
            device.Remote = remote;
            device.LastSeen = now;

            if (!device.Trusted && (!remote || !devices.Values.Any(d => d.Trusted)))
            {
                device.Trusted = true;
                device.Code = "";
            }
            if (!device.Trusted && device.Code == "")
            {
                device.Code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
                askApproval = true;
            }
            devices[id] = device;

            if (fresh || askApproval || now - savedAt > TimeSpan.FromMinutes(5))
                Save();
            return device;
        }
    }

    public DeviceInfo? Approve(string id)
    {
        lock (sync)
        {
            if (!devices.TryGetValue(id, out DeviceInfo? device) || device.Trusted)
                return null;
            device.Trusted = true;
            device.Code = "";
            Save();
            return device;
        }
    }

    public DeviceInfo? ApproveCode(string code)
    {
        DeviceInfo? device;
        lock (sync)
            device = devices.Values.FirstOrDefault(d => !d.Trusted && d.Code == code);
        return device == null ? null : Approve(device.Id);
    }

    public int ApproveAll()
    {
        int count = 0;
        foreach (DeviceInfo device in All().Where(d => !d.Trusted))
        {
            if (Approve(device.Id) != null)
                count++;
        }
        return count;
    }

    public DeviceInfo? Remove(string id)
    {
        lock (sync)
        {
            if (!devices.Remove(id, out DeviceInfo? device))
                return null;
            Save();
            return device;
        }
    }

    public List<DeviceInfo> All()
    {
        lock (sync)
            return devices.Values.OrderByDescending(d => d.LastSeen).ToList();
    }

    private void Save()
    {
        savedAt = DateTime.UtcNow;
        File.WriteAllText(file, JsonSerializer.Serialize(devices.Values.ToList()));
    }

    public static string Describe(string ua)
    {
        string device = ua.Contains("iPhone") ? "iPhone"
            : ua.Contains("iPad") ? "iPad"
            : ua.Contains("Android") ? "Android"
            : ua.Contains("Windows") ? "Windows PC"
            : ua.Contains("Mac OS") ? "Mac"
            : "Unknown device";
        string browser = ua.Contains("Edg/") ? "Edge"
            : ua.Contains("CriOS") || ua.Contains("Chrome/") ? "Chrome"
            : ua.Contains("FxiOS") || ua.Contains("Firefox/") ? "Firefox"
            : ua.Contains("Safari/") ? "Safari"
            : "browser";
        return $"{device} · {browser}";
    }
}
