using System.Runtime.InteropServices;

namespace ZephyrRemote;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct DxgiOutputDesc
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string DeviceName;
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
    public int AttachedToDesktop;
    public int Rotation;
    public IntPtr Monitor;
}
