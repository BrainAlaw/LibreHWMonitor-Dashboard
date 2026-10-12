using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PCMonitor.Infra;

internal sealed record LanUrl(string Name, string Url);

internal static class LanAddresses
{
    /// <summary>Dashboard URLs, with the access token, for every private IPv4 address of this machine.</summary>
    public static IReadOnlyList<LanUrl> Enumerate(int port, string? token)
    {
        List<LanUrl> urls = [];
        try
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces()
                .Where(static a => a.OperationalStatus == OperationalStatus.Up && a.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses
                    .Where(static a => a.Address.AddressFamily == AddressFamily.InterNetwork && Net.IsPrivate(a.Address)))
                {
                    string query = string.IsNullOrEmpty(token) ? string.Empty : $"?token={Uri.EscapeDataString(token)}";
                    urls.Add(new LanUrl(adapter.Name, $"http://{address.Address}:{port}/{query}"));
                }
            }
        }
        catch (Exception error)
        {
            Log.Error("Network interface enumeration failed", error);
        }

        return urls;
    }
}

internal static class Net
{
    /// <summary>RFC 1918 private ranges, link-local (169.254/16) and loopback (127/8).</summary>
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] b = address.GetAddressBytes();
        return b[0] == 10 ||
            b[0] == 127 ||
            (b[0] == 172 && b[1] is >= 16 and <= 31) ||
            (b[0] == 192 && b[1] == 168) ||
            (b[0] == 169 && b[1] == 254);
    }
}
