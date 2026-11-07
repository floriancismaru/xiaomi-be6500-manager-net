using System.Net;
using System.Net.Sockets;

namespace XiaomiBE6500Manager.Core;

public static class IpValidation
{
    public static bool IsValidIpv4(string s) =>
        s.Count(c => c == '.') == 3 && IPAddress.TryParse(s, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork;

    /// <summary>Only private LAN addresses are allowed, so the proxy can't act as an open proxy.</summary>
    public static bool IsPrivateIpv4(string s)
    {
        if (!IsValidIpv4(s)) return false;
        var p = IPAddress.Parse(s).GetAddressBytes();
        return p[0] == 10 || (p[0] == 172 && p[1] is >= 16 and <= 31) || (p[0] == 192 && p[1] == 168);
    }
}
