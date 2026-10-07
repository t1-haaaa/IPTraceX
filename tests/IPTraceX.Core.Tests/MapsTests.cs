using Xunit;
using IPTraceX.Core;

namespace IPTraceX.Core.Tests;

public sealed class MapsTests
{
    [Theory]
    [InlineData(45.8398578, -119.7005791, true)]
    [InlineData(-90.0, -180.0, true)]
    [InlineData(90.0, 180.0, true)]
    [InlineData(91.0, 0.0, false)]
    [InlineData(0.0, 181.0, false)]
    [InlineData(null, 0.0, false)]
    public void CoordinateRanges(object? lat, object? lon, bool expected)
    {
        Assert.Equal(expected, Maps.IsValidCoordinate(lat, lon));
    }

    [Fact]
    public void RejectsStringsAndBools()
    {
        Assert.False(Maps.IsValidCoordinate("abc", "def"));
        Assert.False(Maps.IsValidCoordinate(true, false));
    }

    [Fact]
    public void BuildsClickableUrl()
    {
        Assert.Equal(
            "https://www.google.com/maps?q=45.8398578,-119.7005791",
            Maps.BuildMapsUrl(45.8398578, -119.7005791));
    }

    [Fact]
    public void NoUrlWhenInvalid()
    {
        Assert.Null(Maps.BuildMapsUrl(null, null));
        Assert.Null(Maps.BuildMapsUrl(999.0, 999.0));
    }

    [Fact]
    public void RefusesUnexpectedUrl()
    {
        var (opened, _) = Maps.OpenInBrowser("https://evil.example/x");
        Assert.False(opened);
    }
}
