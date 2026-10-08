using IPTraceX.Core;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class EvidenceTests
{
    private static IntelligenceProfile Profile() => Sample.Profile();

    [Fact]
    public void MatrixShowsEveryVote()
    {
        var profile = Profile();
        profile.Geo.Votes.Add(Sample.Result());
        var rows = Evidence.BuildMatrix(profile);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Provider)));
        Assert.Contains(rows, r => r.Status == EvidenceStatus.Support);
        Assert.DoesNotContain(rows, r => string.IsNullOrWhiteSpace(r.Value));
    }

    [Fact]
    public void MatrixMarksConflictAndMissing()
    {
        var profile = Profile();
        profile.Geo.Votes.Add(Sample.Result());
        var other = Sample.Result();
        other.Source = "other";
        other.Geolocation.Region = "Somewhere Else";
        other.Geolocation.City = null;
        profile.Geo.Votes.Add(other);
        var rows = Evidence.BuildMatrix(profile);
        Assert.Contains(rows, r => r.Field == "Region" && r.Status == EvidenceStatus.Conflict);
        Assert.Contains(rows, r => r.Field == "City" && r.Status == EvidenceStatus.Missing);
        Assert.Contains(rows, r =>
            r.Field == "Country" && r.Status == EvidenceStatus.Support
            && r.Value == "United States");
    }

    [Fact]
    public void ConfidenceExplainsItself()
    {
        var profile = Profile();
        var first = Sample.Result();
        first.Source = "ipwho.is";
        var second = Sample.Result();
        second.Source = "ipapi.co";
        profile.Geo.Votes.Add(first);
        profile.Geo.Votes.Add(second);
        var details = Evidence.ExplainConfidence(profile);
        Assert.Equal(4, details.Count);
        var country = details.First(d => d.Field == "country");
        Assert.Equal("high", country.Level);
        Assert.Contains("agree", country.Reason);
        Assert.NotEmpty(country.SupportingProviders);
        Assert.Empty(country.ConflictingProviders);
    }

    [Fact]
    public void ConfidenceUnknownWithoutData()
    {
        var profile = Profile();
        profile.Geo.Geolocation.Region = null;
        var details = Evidence.ExplainConfidence(profile);
        var region = details.First(d => d.Field == "region");
        Assert.Equal("unknown", region.Level);
    }

    [Fact]
    public void SnapshotTimelineAndComparison()
    {
        var first = Profile();
        var second = Profile();
        second.Geo.Network.Asn = "AS99999";
        var inv1 = new Investigation("IPX-20261001-AAAAAA",
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            "1.1.0", "1.2.3.4", "ip", first, [], "2.1");
        var inv2 = new Investigation("IPX-20261008-BBBBBB",
            new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero),
            "1.1.0", "1.2.3.4", "ip", second, [], "2.1");

        var changes = Evidence.TimelineChanges([inv1, inv2]);
        Assert.Contains(changes, c => c.Contains("ASN") && c.Contains("AS99999"));

        var rows = Evidence.Compare(inv1, inv2);
        var asn = rows.First(r => r.Field == "ASN");
        Assert.True(asn.Changed);
        Assert.Equal("AS16509", asn.A);
        Assert.Equal("AS99999", asn.B);
        var country = rows.First(r => r.Field == "Country");
        Assert.False(country.Changed);

        var same = Evidence.CompareProfiles(first, first);
        Assert.All(same, r => Assert.False(r.Changed));
    }

    [Fact]
    public void ReliabilityTiersDocumented()
    {
        Assert.Equal("HIGH", IPTraceX.Infrastructure.ProviderCatalog.ReliabilityOf("tor-exits"));
        Assert.Equal("MEDIUM", IPTraceX.Infrastructure.ProviderCatalog.ReliabilityOf("ipwho.is"));
        Assert.Equal("UNKNOWN", IPTraceX.Infrastructure.ProviderCatalog.ReliabilityOf("nope"));
    }
}
