using System.Runtime.InteropServices;

namespace ZephyrRemote;

[ComImport, Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDxgiOutput
{
    void SetPrivateData();
    void SetPrivateDataInterface();
    void GetPrivateData();
    void GetParent();

    [PreserveSig]
    int GetDesc(out DxgiOutputDesc desc);
}
