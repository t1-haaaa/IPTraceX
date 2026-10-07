using IPTraceX.Core;

namespace IPTraceX.CLI;

/// <summary>Human-readable report formatting. Data in, text out -- no I/O here.</summary>
public static class Formatting
{
    public static readonly string[] BannerArt =
    [
        @" ___ ____ _____                    __  __",
        @"|_ _|  _ \_   _| __ __ _  ___  ___\ \/ /",
        @" | | | |_) || | | '__/ _` |/ __|/ _ \  /",
        @" | | |  __/ | | | | | (_| | (__  __/ /  \",
        @"|___|_|    |_||_|  \__,_|\___\___/_/\_\",
    ];

    public const string BannerSubtitle = "MULTI-PROVIDER IP INTELLIGENCE & GEOLOCATION CLI";

    private static string V(object? value)
    {
        if (value is null)
        {
            return "Unknown";
        }

        if (value is double d)
        {
            return d.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        }

        string text = value.ToString()?.Trim() ?? "";
        return text.Length == 0 ? "Unknown" : text;
    }

    public static string AsciiBanner(Palette p)
    {
        int width = BannerArt.Max(line => line.Length);
        var lines = BannerArt.Select(line => p.Brand(line)).ToList();
        lines.Add(p.Data(BannerSubtitle.PadLeft((width + BannerSubtitle.Length) / 2).PadRight(width)));
        return string.Join("\n", lines);
    }

    public static string Startup(Palette p, string version)
        => "\n" + AsciiBanner(p) + "\n\n"
            + $"{p.TokenInfo()} Multi-provider IP intelligence & geolocation CLI\n"
            + $"{p.TokenInfo()} Version {version}\n"
            + $"{p.TokenOk()} Status: Ready\n";

    public static string? StageLine(Palette p, string stage)
        => stage switch
        {
            "validating" => $"{p.TokenInfo()} Validating IP...",
            "querying" => $"{p.TokenInfo()} Querying GeoIP providers...",
            "cached" => $"{p.TokenInfo()} Loading cached result...",
            "done" => $"{p.TokenOk()} Lookup completed.",
            _ => null,
        };

    public static string FormatReport(GeoResult info, Palette p)
    {
        var g = info.Geolocation;
        var n = info.Network;
        string version = info.IpVersion == 6 ? "IPv6" : "IPv4";
        var lines = new List<string>
        {
            "",
            $"{p.TokenOk()} {p.Brand("IP INFORMATION")}",
            $"    Address      : {p.Data(info.Ip)}",
            $"    Version      : {p.Data(version)}",
            $"    Type         : {p.Data("Public")}",
            "",
            $"{p.TokenOk()} {p.Brand("GEOLOCATION")}",
            $"    Country      : {p.Data(V(g.Country))}",
            $"    Country Code : {p.Data(V(g.CountryCode))}",
            $"    Region       : {p.Data(V(g.Region))}",
            $"    Region Code  : {p.Data(V(g.RegionCode))}",
            $"    City         : {p.Data(V(g.City))}",
            $"    Postal Code  : {p.Data(V(g.PostalCode))}",
            $"    Latitude     : {p.Data(V(g.Latitude))}",
            $"    Longitude    : {p.Data(V(g.Longitude))}",
            $"    Timezone     : {p.Data(V(g.Timezone))}",
            "",
            $"{p.TokenOk()} {p.Brand("NETWORK")}",
            $"    ISP          : {p.Data(V(n.Isp))}",
            $"    Organization : {p.Data(V(n.Organization))}",
            $"    ASN          : {p.Data(V(n.Asn))}",
            $"    AS Name      : {p.Data(V(n.AsName))}",
            $"    Hostname     : {p.Data(V(n.Hostname))}",
            "",
        };
        if (info.GoogleMapsUrl is not null)
        {
            lines.Add($"{p.TokenOk()} {p.Brand("GOOGLE MAPS")}");
            lines.Add("");
            lines.Add($"    {p.Data(info.GoogleMapsUrl)}");
            lines.Add("");
        }
        else
        {
            lines.Add($"{p.TokenWarn()} Google Maps location unavailable.");
            lines.Add("");
        }

        lines.Add($"{p.TokenOk()} {p.Brand("GEOIP QUALITY")}");
        lines.Add($"    Providers    : {p.Data($"{info.ProvidersSuccessful}/{info.ProvidersQueried}")}");
        lines.Add($"    Agreement    : {p.Data($"{info.ProvidersAgreeing}/{info.ProvidersSuccessful}")}");
        lines.Add($"    Confidence   : {p.Data(info.Confidence.ToUpperInvariant())}");
        lines.Add($"    Source       : {p.Data(string.IsNullOrEmpty(info.Source) ? "Unknown" : info.Source)}");
        lines.Add("");
        if (info.Disputed)
        {
            lines.Add($"{p.TokenWarn()} GEOIP PROVIDER DISAGREEMENT");
            lines.Add("");
            foreach (ProviderDetail detail in info.Providers)
            {
                if (detail.Status == "success")
                {
                    string summary = detail.Summary.Length == 0 ? "Unknown" : detail.Summary;
                    lines.Add($"    {p.Data(detail.Name.PadRight(12))} : {summary}");
                }
                else
                {
                    lines.Add($"    {detail.Name.PadRight(12)} : failed");
                }
            }

            lines.Add("");
        }

        lines.Add(p.TokenWarn() + " NOTE");
        lines.Add("    IP geolocation is approximate.");
        lines.Add("    It does not identify an exact physical address,");
        lines.Add("    person, or real-time device location.");
        lines.Add("");
        return string.Join("\n", lines);
    }

    public static string InteractiveMenu(Palette p, bool mapsAvailable)
    {
        var lines = new List<string>
        {
            "",
            $"{p.TokenInfo()} Actions",
            "",
            "[01] Analyze another IP",
            mapsAvailable
                ? "[02] Open location in Google Maps"
                : "[02] Open location in Google Maps (unavailable)",
            "[03] Export JSON",
            "[04] Save report",
            "[00] Exit",
            "",
            $"{p.TokenAsk()} Select an option:",
        };
        return string.Join("\n", lines);
    }

    public static string InputPrompt(Palette p)
        => $"{p.TokenAsk()} Enter public IP address:\n{p.TokenIn()} ";

    public static string BatchHeader(Palette p, int total)
        => $"{p.TokenInfo()} Processing {total} IP addresses...";

    public static string BatchItem(Palette p, int index)
        => p.Data($"[{index:D2}]");

    public static string BatchSummary(Palette p, int ok, int failed)
        => $"{p.TokenInfo()} Completed: {ok}\n{p.TokenInfo()} Failed: {failed}";
}
