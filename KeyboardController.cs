using System.Runtime.InteropServices;

namespace ZephyrRemote;

public class KeyboardController
{
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;
    private const uint Extended = 0x0001;

    private static readonly string[] ExtendedKeys = { "left", "up", "right", "down", "delete", "win" };

    private static readonly Dictionary<string, ushort> Keys = new()
    {
        ["enter"] = 0x0D,
        ["backspace"] = 0x08,
        ["tab"] = 0x09,
        ["esc"] = 0x1B,
        ["space"] = 0x20,
        ["left"] = 0x25,
        ["up"] = 0x26,
        ["right"] = 0x27,
        ["down"] = 0x28,
        ["delete"] = 0x2E,
        ["win"] = 0x5B,
        ["ctrl"] = 0x11,
        ["shift"] = 0x10,
        ["alt"] = 0x12,
        ["c"] = 0x43,
        ["v"] = 0x56,
        ["a"] = 0x41,
        ["z"] = 0x5A
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)]
        public uint Type;
        [FieldOffset(8)]
        public KeyInput Key;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char c);

    public void Type(string text)
    {
        foreach (char c in text)
        {
            if (c == '\n')
            {
                Press("enter");
                continue;
            }
            short scan = VkKeyScan(c);
            int modifiers = (scan >> 8) & 0xFF;
            if (c < 128 && scan != -1 && (modifiers & ~1) == 0)
            {
                ushort vk = (ushort)(scan & 0xFF);
                bool shift = (modifiers & 1) != 0;
                if (shift)
                    Send(0x10, 0, 0);
                Send(vk, 0, 0);
                Send(vk, 0, KeyUp);
                if (shift)
                    Send(0x10, 0, KeyUp);
                continue;
            }
            Send(0, c, Unicode);
            Send(0, c, Unicode | KeyUp);
        }
    }

    public bool Press(string combo)
    {
        string[] parts = combo.ToLower().Split('+');
        if (parts.Any(p => !Keys.ContainsKey(p)))
            return false;

        foreach (string part in parts)
            Send(Keys[part], 0, Flags(part));
        foreach (string part in Enumerable.Reverse(parts))
            Send(Keys[part], 0, Flags(part) | KeyUp);
        return true;
    }

    private uint Flags(string key)
    {
        return ExtendedKeys.Contains(key) ? Extended : 0;
    }

    private void Send(ushort vk, ushort scan, uint flags)
    {
        Input[] inputs =
        {
            new Input { Type = 1, Key = new KeyInput { Vk = vk, Scan = scan, Flags = flags } }
        };
        SendInput(1, inputs, Marshal.SizeOf<Input>());
    }
}
