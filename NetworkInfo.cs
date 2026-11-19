using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ZephyrRemote;

public class NetworkInfo
{
    public string LocalIp { get; private set; } = "localhost";
    public string MacAddress { get; private set; } = "";

    public NetworkInfo()
    {
        NetworkInterface? nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && n.GetIPProperties().GatewayAddresses.Count > 0);

        if (nic == null)
            return;

        MacAddress = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));

        UnicastIPAddressInformation? ip = nic.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

        if (ip != null)
            LocalIp = ip.Address.ToString();
    }
}
