using System.Net;
using PCMonitor.Server;
using Xunit;

namespace PCMonitor.Tests;

public sealed class AccessPolicyTests
{
    private const int Port = 8096;
    private const string Machine = "DESKTOP-1";
    private const string Token = "secret-token";

    private static readonly IPAddress Loopback = IPAddress.Loopback;
    private static readonly IPAddress LanClient = IPAddress.Parse("192.168.1.50");
    private static readonly IPAddress PublicClient = IPAddress.Parse("8.8.8.8");

    private static AccessDecision Check(
        string host,
        IPAddress? remote,
        bool allowLan = false,
        string? token = Token,
        string? presented = null,
        int hostPort = Port) =>
        AccessPolicy.Evaluate(host, hostPort, remote, Port, allowLan, Machine, token, presented);

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    public void LoopbackClientWithLoopbackHostIsAllowedWithoutToken(string host)
    {
        Assert.Equal(AccessDecision.Allow, Check(host, Loopback));
    }

    [Fact]
    public void LoopbackClientIsAllowedWhenReachingTheServerThroughTheIpv4MappedAddress()
    {
        Assert.Equal(AccessDecision.Allow, Check("127.0.0.1", Loopback.MapToIPv6()));
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("192.168.1.10")]
    public void RebindingStyleHostsAreRejectedWhenLanIsOff(string host)
    {
        Assert.Equal(AccessDecision.ForbiddenHost, Check(host, Loopback));
    }

    [Fact]
    public void WrongPortIsRejected()
    {
        Assert.Equal(AccessDecision.ForbiddenHost, Check("127.0.0.1", Loopback, hostPort: 9999));
    }

    [Fact]
    public void RemoteClientsAreRejectedWhenLanIsOff()
    {
        Assert.Equal(AccessDecision.ForbiddenHost, Check("192.168.1.10", LanClient, presented: Token));
    }

    [Fact]
    public void LanClientWithoutTokenIsUnauthorized()
    {
        Assert.Equal(AccessDecision.Unauthorized, Check("192.168.1.10", LanClient, allowLan: true));
    }

    [Fact]
    public void LanClientWithWrongTokenIsUnauthorized()
    {
        Assert.Equal(AccessDecision.Unauthorized, Check("192.168.1.10", LanClient, allowLan: true, presented: "nope"));
    }

    [Theory]
    [InlineData("192.168.1.10")]
    [InlineData("10.0.0.5")]
    [InlineData("172.20.1.1")]
    [InlineData("DESKTOP-1")]
    [InlineData("desktop-1.local")]
    public void LanClientWithTheTokenIsAllowedOnLanHosts(string host)
    {
        Assert.Equal(AccessDecision.Allow, Check(host, LanClient, allowLan: true, presented: Token));
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("8.8.8.8")]
    public void LanClientWithNonLanHostIsRejected(string host)
    {
        Assert.Equal(AccessDecision.ForbiddenHost, Check(host, LanClient, allowLan: true, presented: Token));
    }

    [Fact]
    public void PublicClientsAreRejectedEvenWithTheToken()
    {
        Assert.Equal(AccessDecision.ForbiddenHost, Check("192.168.1.10", PublicClient, allowLan: true, presented: Token));
    }

    [Fact]
    public void LanModeWithoutAConfiguredTokenRefusesRemoteClients()
    {
        Assert.Equal(AccessDecision.ForbiddenHost, Check("192.168.1.10", LanClient, allowLan: true, token: null, presented: "anything"));
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.255.255", true)]
    [InlineData("169.254.10.10", true)]
    [InlineData("100.64.0.1", false)]
    [InlineData("1.1.1.1", false)]
    public void ClassifiesPrivateRanges(string address, bool expected)
    {
        Assert.Equal(expected, PCMonitor.Infra.Net.IsPrivate(IPAddress.Parse(address)));
    }

    [Fact]
    public void GeneratedTokensAreUniqueAndUrlSafe()
    {
        string a = AccessPolicy.NewToken();
        string b = AccessPolicy.NewToken();

        Assert.NotEqual(a, b);
        Assert.Matches("^[A-Za-z0-9_-]{22}$", a);
    }

    [Fact]
    public void TokenComparisonRejectsMissingAndDifferentValues()
    {
        Assert.True(AccessPolicy.TokensMatch("abc", "abc"));
        Assert.False(AccessPolicy.TokensMatch("abc", "abd"));
        Assert.False(AccessPolicy.TokensMatch("abc", null));
        Assert.False(AccessPolicy.TokensMatch("abc", ""));
    }
}
