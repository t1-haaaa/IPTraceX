using Xunit;
using IPTraceX.Core;

namespace IPTraceX.Core.Tests;

public sealed class ConsensusTests
{
    private static GeoResult R(
        string country = "Algeria", string? code = "DZ",
        string? region = "Bechar", string? regionCode = "08", string? city = "Bechar",
        double? lat = 31.616671, double? lon = -2.21667, string? tz = "Africa/Algiers",
        string? isp = "Telecom Algeria", string? asn = "AS36947", string source = "test")
    {
        var r = new GeoResult { Ip = "41.107.85.239", IpVersion = 4, Source = source };
        r.Geolocation.Country = country;
        r.Geolocation.CountryCode = code;
        r.Geolocation.Region = region;
        r.Geolocation.RegionCode = regionCode;
        r.Geolocation.City = city;
        r.Geolocation.Latitude = lat;
        r.Geolocation.Longitude = lon;
        r.Geolocation.Timezone = tz;
        r.Network.Isp = isp;
        r.Network.Organization = isp;
        r.Network.Asn = asn;
        return r;
    }

    [Fact]
    public void UnanimousIsHigh()
    {
        var outcome = Consensus.Build([R(), R(), R()]);
        Assert.Equal("Algeria", outcome.Final.Geolocation.Country);
        Assert.Equal("DZ", outcome.Final.Geolocation.CountryCode);
        Assert.Equal("high", outcome.Confidence);
        Assert.Equal(3, outcome.Agreeing);
        Assert.False(outcome.Disputed);
    }

    [Fact]
    public void TwoOfThreeIsMedium()
    {
        var outcome = Consensus.Build([R(), R(), R(country: "France", code: "FR")]);
        Assert.Equal("Algeria", outcome.Final.Geolocation.Country);
        Assert.Equal("medium", outcome.Confidence);
        Assert.Equal(2, outcome.Agreeing);
        Assert.True(outcome.Disputed);
    }

    [Fact]
    public void OneOfThreeIsLow()
    {
        var outcome = Consensus.Build([
            R(), R(country: "France", code: "FR"), R(country: "Germany", code: "DE")]);
        Assert.Equal("low", outcome.Confidence);
        Assert.Equal(1, outcome.Agreeing);
        Assert.True(outcome.Disputed);
    }

    [Fact]
    public void SingleSourceIsMedium()
    {
        var outcome = Consensus.Build([R()]);
        Assert.Equal("medium", outcome.Confidence);
        Assert.False(outcome.Disputed);
    }

    [Fact]
    public void RegionMajorityAndMissing()
    {
        var outcome = Consensus.Build([R(region: "Bechar"), R(region: "Bechar"), R(region: null)]);
        Assert.Equal("Bechar", outcome.Final.Geolocation.Region);
        outcome = Consensus.Build([R(region: null), R(region: null)]);
        Assert.Null(outcome.Final.Geolocation.Region);
    }

    [Fact]
    public void RegionSplitLowersAgreement()
    {
        var outcome = Consensus.Build([
            R(region: "Bechar", city: "Bechar"),
            R(region: "Bechar", city: "Bechar"),
            R(region: "Adrar", city: "Adrar")]);
        Assert.True(outcome.Disputed);
        Assert.Equal("Bechar", outcome.Final.Geolocation.Region);
        Assert.Equal(2, outcome.Agreeing);
        Assert.Equal("medium", outcome.Confidence);
    }

    [Fact]
    public void CityMatchesCaseInsensitively()
    {
        var outcome = Consensus.Build([R(city: "Bechar"), R(city: "BECHAR")]);
        Assert.Equal("bechar", outcome.Final.Geolocation.City?.ToLowerInvariant());
    }

    [Fact]
    public void CoordinatesClusterToMean()
    {
        var outcome = Consensus.Build([
            R(lat: 31.61, lon: -2.21), R(lat: 31.62, lon: -2.22), R(lat: 31.60, lon: -2.20)]);
        Assert.InRange(outcome.Final.Geolocation.Latitude!.Value, 31.59, 31.63);
        Assert.NotNull(outcome.Final.GoogleMapsUrl);
    }

    [Fact]
    public void CoordinateOutlierIgnored()
    {
        var outcome = Consensus.Build([
            R(lat: 31.61, lon: -2.21), R(lat: 31.62, lon: -2.22), R(lat: 48.85, lon: 2.35)]);
        Assert.True(Math.Abs(outcome.Final.Geolocation.Latitude!.Value - 31.6) < 1.0);
    }

    [Fact]
    public void InvalidCoordinatesDropped()
    {
        var outcome = Consensus.Build([R(lat: 999.0, lon: 999.0), R(lat: null, lon: null)]);
        Assert.Null(outcome.Final.Geolocation.Latitude);
        Assert.Null(outcome.Final.GoogleMapsUrl);
    }

    [Fact]
    public void HaversineSanity()
    {
        Assert.True(Consensus.HaversineKm(31.61, -2.21, 31.62, -2.22) < 5.0);
        Assert.True(Consensus.HaversineKm(31.61, -2.21, 48.85, 2.35) > 1000.0);
    }

    [Fact]
    public void IspNormalizationVotesTogether()
    {
        Assert.NotNull(Consensus.MajorityIsp(["Telecom Algeria", "telecom algeria ", "TELECOM-ALGERIA"]));
        Assert.Equal("Google LLC", Consensus.MajorityIsp(["Google LLC", "Google"]));
    }

    [Fact]
    public void AsnDigitsMatch()
    {
        Assert.Equal("AS15169", Consensus.MajorityAsn(["AS15169", "15169"]));
        Assert.Null(Consensus.MajorityAsn([null, null]));
    }

    [Fact]
    public void EmptyResultsRejected()
    {
        Assert.Throws<ArgumentException>(() => Consensus.Build([]));
    }
}
