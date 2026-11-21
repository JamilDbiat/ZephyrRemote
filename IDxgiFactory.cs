using System.Runtime.InteropServices;

namespace ZephyrRemote;

[ComImport, Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDxgiFactory
{
    void SetPrivateData();
    void SetPrivateDataInterface();
    void GetPrivateData();
    void GetParent();

    [PreserveSig]
    int EnumAdapters(uint index, out IDxgiAdapter adapter);
}
