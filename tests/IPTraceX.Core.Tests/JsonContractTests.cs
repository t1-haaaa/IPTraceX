using Xunit;
using System.Text.Json;
using System.Text.Json.Nodes;
using IPTraceX.CLI;
using IPTraceX.Core;

namespace IPTraceX.Core.Tests;

public sealed class JsonContractTests
{
    [Fact]
    public void LegacyKeysIntactGeolocationShapeFrozen()
    {
        JsonObject data = GeoJson.FromResult(Sample.Result());
        Assert.True(new[] { "ip", "ip_version", "geolocation", "network", "google_maps_url" }
            .All(k => data.ContainsKey(k)));
        Assert.Equal(
            new[] { "country", "country_code", "region", "region_code", "city", "postal_code", "latitude", "longitude", "timezone" },
            data["geolocation"]!.AsObject().Select(p => p.Key).ToArray());
        Assert.Equal(
            new[] { "isp", "organization", "asn", "as_name", "hostname" },
            data["network"]!.AsObject().Select(p => p.Key).ToArray());
    }

    [Fact]
    public void AdditiveQualityFields()
    {
        JsonObject data = GeoJson.FromResult(Sample.Result());
        JsonObject quality = data["geoip_quality"]!.AsObject();
        Assert.Equal(3, quality["providers_queried"]!.GetValue<int>());
        Assert.Equal(3, quality["providers_successful"]!.GetValue<int>());
        Assert.Equal(3, quality["providers_agreeing"]!.GetValue<int>());
        Assert.Equal("high", quality["confidence"]!.GetValue<string>());
        Assert.Equal(1.0, quality["agreement_ratio"]!.GetValue<double>());
        Assert.False(quality["disputed"]!.GetValue<bool>());
        Assert.Equal("Multi-provider consensus", quality["source"]!.GetValue<string>());
        Assert.IsType<JsonArray>(data["providers"]);
    }

    [Fact]
    public void MissingFieldsAreNullNotAbsent()
    {
        // NOTE: System.Text.Json.Nodes represents JSON null as C# null
        // inside containers, so a null assertion IS the null check here.
        JsonObject data = GeoJson.FromResult(new GeoResult { Ip = "1.1.1.1", IpVersion = 4 });
        JsonObject geo = data["geolocation"]!.AsObject();
        Assert.Null(geo["country"]);
        Assert.Null(data["google_maps_url"]);
        string compact = data.ToJsonString().Replace(" ", "");
        Assert.Contains("\"country\":null", compact);
        Assert.Contains("\"google_maps_url\":null", compact);
    }

    [Fact]
    public void CliJsonTextIsPure()
    {
        string text = CliApp.JsonText(Sample.Result());
        Assert.Equal(-1, text.IndexOf("\u001b", StringComparison.Ordinal));
        using var doc = System.Text.Json.JsonDocument.Parse(text);
        Assert.Equal("35.94.45.221", doc.RootElement.GetProperty("ip").GetString());
    }
}
