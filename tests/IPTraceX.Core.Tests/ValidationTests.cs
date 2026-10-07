using Xunit;
using IPTraceX.Core;

namespace IPTraceX.Core.Tests;

public sealed class ValidationTests
{
    [Theory]
    [InlineData("8.8.8.8", 4)]
    [InlineData("1.1.1.1", 4)]
    [InlineData("35.94.45.221", 4)]
    [InlineData("41.107.85.239", 4)]
    [InlineData("2001:4860:4860::8888", 6)]
    [InlineData("2a03:2880:15ff:54::", 6)]
    public void ValidIpsParse(string ip, int version)
    {
        var parsed = IpValidation.ParseIp(ip);
        Assert.Equal(version, parsed.Version);
    }

    [Theory]
    [InlineData("999.999.999.999")]
    [InlineData("not-an-ip")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("8.8.8.8/24")]
    [InlineData("8.8.8.8 extra")]
    [InlineData("fe80::1%eth0")]
    public void InvalidIpsRejected(string ip)
    {
        Assert.Throws<InvalidIpException>(() => IpValidation.ParseIp(ip));
    }

    [Theory]
    [InlineData("192.168.1.1", "private")]
    [InlineData("10.0.0.1", "private")]
    [InlineData("172.16.5.4", "private")]
    [InlineData("100.64.0.1", "private")]
    [InlineData("127.0.0.1", "loopback")]
    [InlineData("::1", "loopback")]
    [InlineData("169.254.10.20", "link-local")]
    [InlineData("fe80::1", "link-local")]
    [InlineData("224.0.0.1", "multicast")]
    [InlineData("ff02::1", "multicast")]
    [InlineData("240.0.0.1", "reserved")]
    [InlineData("0.0.0.0", "unspecified")]
    [InlineData("::", "unspecified")]
    [InlineData("fc00::1", "private")]
    public void NonPublicRejectedWithReason(string ip, string reasonFragment)
    {
        var ex = Assert.Throws<NonPublicIpException>(() => IpValidation.EnsurePublic(ip));
        Assert.Contains(reasonFragment, ex.Message);
    }

    [Fact]
    public void PublicPasses()
    {
        Assert.Equal(4, IpValidation.EnsurePublic("8.8.8.8").Version);
        Assert.Equal(6, IpValidation.EnsurePublic("2a03:2880:15ff:54::").Version);
    }

    [Fact]
    public void BracketedIpv6Accepted()
    {
        Assert.Equal(6, IpValidation.ParseIp("[::1]").Version);
    }

    // ---- Input-encoding regression (former \x96\x83\x96 incident) ----

    [Fact]
    public void ControlPrefixStripped()
    {
        string poisoned = "\u0096\u0083\u009635.94.45.221";
        Assert.Equal("35.94.45.221", IpValidation.ParseIp(poisoned).Text);
        Assert.Equal("35.94.45.221", IpValidation.EnsurePublic(poisoned).Text);
    }

    [Fact]
    public void BidiBomNbspEdgesStripped()
    {
        Assert.Equal("35.94.45.221", IpValidation.ParseIp(" 35.94.45.221 ").Text);
        Assert.Equal("35.94.45.221", IpValidation.ParseIp("\u200f35.94.45.221\u200e").Text);
        Assert.Equal("8.8.8.8", IpValidation.ParseIp("\ufeff8.8.8.8").Text);
        Assert.Equal("1.1.1.1", IpValidation.ParseIp("\u00a01.1.1.1\u00a0").Text);
        Assert.Equal("8.8.8.8", IpValidation.ParseIp("\ufffd8.8.8.8").Text);
    }

    [Fact]
    public void InteriorGarbageStaysInvalid()
    {
        Assert.Throws<InvalidIpException>(() => IpValidation.ParseIp("35.94.\u200b45.221"));
        Assert.Throws<InvalidIpException>(() => IpValidation.ParseIp("--8.8.8.8"));
        Assert.Throws<InvalidIpException>(() => IpValidation.ParseIp("8.8.8.8 extra"));
    }

    [Fact]
    public void CleanIpTextEdgesOnly()
    {
        Assert.Equal("", IpValidation.CleanIpText(null));
        Assert.Equal("", IpValidation.CleanIpText("   "));
        Assert.Throws<InvalidIpException>(() => IpValidation.CleanIpText(12345));
    }
}
