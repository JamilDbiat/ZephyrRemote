using System.Runtime.InteropServices;

namespace ZephyrRemote;

[ComImport, Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDxgiAdapter
{
    void SetPrivateData();
    void SetPrivateDataInterface();
    void GetPrivateData();
    void GetParent();

    [PreserveSig]
    int EnumOutputs(uint index, out IDxgiOutput output);
}
