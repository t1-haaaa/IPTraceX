using Xunit;
using System.Text.Json;
using IPTraceX.Core;
using IPTraceX.Infrastructure.Providers;

namespace IPTraceX.Core.Tests;

public sealed class ProviderNormalizationTests
{
    private static JsonElement Doc(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void IpWhoIsNormalization()
    {
        var info = IpWhoIsProvider.Normalize("8.8.8.8", Doc(
            """{"ip":"8.8.8.8","success":true,"type":"IPv4","country":"United States","country_code":"US","region":"California","region_code":"CA","city":"Mountain View","postal":"94043","latitude":37.386,"longitude":-122.0838,"timezone":{"id":"America/Los_Angeles"},"connection":{"asn":15169,"org":"Google LLC","isp":"Google LLC"}}"""));
        Assert.Equal("8.8.8.8", info.Ip);
        Assert.Equal(4, info.IpVersion);
        Assert.Equal("United States", info.Geolocation.Country);
        Assert.Equal("AS15169", info.Network.Asn);
        Assert.NotNull(info.GoogleMapsUrl);
        Assert.Contains("google.com/maps", info.GoogleMapsUrl);
        Assert.Equal("ipwho.is", info.Source);
    }

    [Fact]
    public void IpApiCoNormalization()
    {
        var info = IpApiCoProvider.Normalize("41.107.85.239", Doc(
            """{"ip":"41.107.85.239","country_name":"Algeria","country_code":"DZ","region":"Tindouf","region_code":"37","city":"Tindouf","postal":"37001","latitude":27.67111,"longitude":-8.14744,"timezone":"Africa/Algiers","org":"Telecom Algeria","asn":"AS36947"}"""));
        Assert.Equal("Algeria", info.Geolocation.Country);
        Assert.Equal("Tindouf", info.Geolocation.Region);
        Assert.Equal("AS36947", info.Network.Asn);
        Assert.Equal("ipapi.co", info.Source);
    }

    [Fact]
    public void IpApiCoRateLimitShape()
    {
        Assert.Throws<RateLimitException>(() => IpApiCoProvider.Normalize(
            "8.8.8.8", Doc("""{"error":true,"reason":"RateLimited, please throttle your requests"}""")));
    }

    [Fact]
    public void IpInfoNormalization()
    {
        var info = IpInfoProvider.Normalize("8.8.8.8", Doc(
            """{"ip":"8.8.8.8","hostname":"dns.google","city":"Mountain View","region":"California","country":"US","loc":"38.0088,-122.1175","org":"AS15169 Google LLC","postal":"94043","timezone":"America/Los_Angeles"}"""));
        Assert.Equal("US", info.Geolocation.CountryCode);
        Assert.Null(info.Geolocation.Country); // code only; never invented
        Assert.Equal("AS15169", info.Network.Asn);
        Assert.Equal("Google LLC", info.Network.Isp);
        Assert.Equal("dns.google", info.Network.Hostname);
        Assert.Equal("ipinfo.io", info.Source);
    }

    [Fact]
    public void IpInfoOrgSplitting()
    {
        Assert.Equal(("AS15169", "Google LLC"), IpInfoProvider.SplitOrg("AS15169 Google LLC"));
        Assert.Equal((null, "Example"), IpInfoProvider.SplitOrg("Example"));
        Assert.Equal((null, null), IpInfoProvider.SplitOrg("  "));
    }

    [Fact]
    public void MissingFieldsDoNotCrash()
    {
        var a = IpWhoIsProvider.Normalize("9.9.9.9", Doc("""{"ip":"9.9.9.9","success":true,"type":"IPv4"}"""));
        Assert.Null(a.Geolocation.Country);
        Assert.Null(a.GoogleMapsUrl);
        var b = IpApiCoProvider.Normalize("9.9.9.9", Doc("""{"ip":"9.9.9.9"}"""));
        Assert.Null(b.Geolocation.Country);
        var c = IpInfoProvider.Normalize("9.9.9.9", Doc("""{"ip":"9.9.9.9","loc":"bogus"}"""));
        Assert.Null(c.Geolocation.Latitude);
    }

    [Fact]
    public void NonObjectPayloadRejected()
    {
        Assert.Throws<BadResponseException>(
            () => IpWhoIsProvider.Normalize("8.8.8.8", Doc("[1,2]")));
    }

    [Fact]
    public void UnsuccessfulIpWhoIsIsNotFound()
    {
        Assert.Throws<NotFoundException>(() => IpWhoIsProvider.Normalize(
            "8.8.8.8", Doc("""{"success":false,"message":"invalid IP"}""")));
    }
}
