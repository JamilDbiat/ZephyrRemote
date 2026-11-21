using System.Runtime.InteropServices;

namespace ZephyrRemote;

public static class ScreenMap
{
    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDxgiFactory factory);

    private static List<Screen> cached = new();
    private static DateTime cachedAt = DateTime.MinValue;

    public static List<Screen> Ordered()
    {
        if (DateTime.UtcNow - cachedAt < TimeSpan.FromSeconds(5) && cached.Count > 0)
            return cached;

        Screen[] all = Screen.AllScreens;
        List<Screen> ordered = new();
        foreach (string name in VideoOutputs())
        {
            Screen? match = all.FirstOrDefault(s => string.Equals(s.DeviceName, name, StringComparison.OrdinalIgnoreCase));
            if (match != null && !ordered.Contains(match))
                ordered.Add(match);
        }
        foreach (Screen screen in all)
        {
            if (!ordered.Contains(screen))
                ordered.Add(screen);
        }
        cached = ordered;
        cachedAt = DateTime.UtcNow;
        return ordered;
    }

    private static List<string> VideoOutputs()
    {
        List<string> names = new();
        try
        {
            Guid factoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");
            if (CreateDXGIFactory1(ref factoryId, out IDxgiFactory factory) != 0)
                return names;
            if (factory.EnumAdapters(0, out IDxgiAdapter adapter) != 0)
                return names;
            for (uint i = 0; adapter.EnumOutputs(i, out IDxgiOutput output) == 0; i++)
            {
                if (output.GetDesc(out DxgiOutputDesc desc) == 0)
                    names.Add(desc.DeviceName);
            }
        }
        catch (Exception)
        {
        }
        return names;
    }
}
