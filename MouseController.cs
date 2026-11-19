using System.Runtime.InteropServices;

namespace ZephyrRemote;

public class MouseController
{
    private const uint Move = 0x0001;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;
    private const uint Wheel = 0x0800;
    private const uint Absolute = 0x8000;
    private const uint VirtualDesk = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public int Data;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);

    public bool MoveTo(int screen, double x, double y)
    {
        Screen[] screens = Screen.AllScreens;
        if (screen < 0 || screen >= screens.Length || x < 0 || x > 1 || y < 0 || y > 1)
            return false;

        Rectangle bounds = screens[screen].Bounds;
        Rectangle desk = SystemInformation.VirtualScreen;
        int px = bounds.Left + (int)(x * bounds.Width);
        int py = bounds.Top + (int)(y * bounds.Height);
        int ax = (int)((px - desk.Left) * 65535.0 / (desk.Width - 1));
        int ay = (int)((py - desk.Top) * 65535.0 / (desk.Height - 1));

        Send(ax, ay, 0, Move | Absolute | VirtualDesk);
        return true;
    }

    public void Press(string button, bool down)
    {
        bool right = button == "right";
        if (down)
            Send(0, 0, 0, right ? RightDown : LeftDown);
        else
            Send(0, 0, 0, right ? RightUp : LeftUp);
    }

    public bool Click(int screen, double x, double y, string button)
    {
        if (!MoveTo(screen, x, y))
            return false;

        Thread.Sleep(20);
        Send(1, 1, 0, Move);
        Thread.Sleep(20);
        Send(-1, -1, 0, Move);
        Thread.Sleep(30);

        Press(button, true);
        Thread.Sleep(60);
        Press(button, false);
        return true;
    }

    public void Wake()
    {
        SetThreadExecutionState(0x00000002);
        Send(2, 0, 0, Move);
        Thread.Sleep(20);
        Send(-2, 0, 0, Move);
    }

    public void Scroll(int amount)
    {
        Send(0, 0, amount, Wheel);
    }

    private void Send(int dx, int dy, int data, uint flags)
    {
        Input[] inputs =
        {
            new Input { Type = 0, Mouse = new MouseInput { Dx = dx, Dy = dy, Data = data, Flags = flags } }
        };
        SendInput(1, inputs, Marshal.SizeOf<Input>());
    }
}
