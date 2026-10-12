using System.Net;
using System.Security.Cryptography;
using System.Text;
using PCMonitor.Infra;

namespace PCMonitor.Server;

internal enum AccessDecision
{
    Allow,
    ForbiddenHost,
    Unauthorized
}

/// <summary>
/// Who may talk to the dashboard. Loopback clients are always trusted (with a loopback Host header, which
/// stops DNS rebinding). With LAN access on, private-network clients may connect with the access token.
/// </summary>
internal static class AccessPolicy
{
    public static AccessDecision Evaluate(
        string? hostName,
        int? hostPort,
        IPAddress? remote,
        int listenPort,
        bool allowLan,
        string machineName,
        string? accessToken,
        string? presentedToken)
    {
        if (hostName is null || hostPort != listenPort)
        {
            return AccessDecision.ForbiddenHost;
        }

        bool remoteIsLoopback = remote is not null && IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote);
        bool loopbackHost = hostName.Equals("127.0.0.1", StringComparison.Ordinal) ||
            hostName.Equals("localhost", StringComparison.OrdinalIgnoreCase);

        if (remoteIsLoopback)
        {
            return loopbackHost || (allowLan && IsLanHost(hostName, machineName))
                ? AccessDecision.Allow
                : AccessDecision.ForbiddenHost;
        }

        if (!allowLan || remote is null || !Net.IsPrivate(remote) || !IsLanHost(hostName, machineName))
        {
            return AccessDecision.ForbiddenHost;
        }

        // LAN mode without a token would be an open door; treat it as misconfigured and refuse.
        if (string.IsNullOrEmpty(accessToken))
        {
            return AccessDecision.ForbiddenHost;
        }

        return TokensMatch(accessToken, presentedToken) ? AccessDecision.Allow : AccessDecision.Unauthorized;
    }

    public static bool TokensMatch(string expected, string? presented)
    {
        if (string.IsNullOrEmpty(presented))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
    }

    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(16));

    private static bool IsLanHost(string hostName, string machineName)
    {
        if (IPAddress.TryParse(hostName, out IPAddress? address))
        {
            return Net.IsPrivate(address);
        }

        return hostName.Equals(machineName, StringComparison.OrdinalIgnoreCase) ||
            hostName.Equals(machineName + ".local", StringComparison.OrdinalIgnoreCase);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
