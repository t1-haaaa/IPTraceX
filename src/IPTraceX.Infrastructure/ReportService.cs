using System.Text.RegularExpressions;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure;

/// <summary>
/// Investigation reports: txt, json, html (md/csv planned).
/// Stored under reports/&lt;yyyy-MM-dd&gt;/ with safe filenames.
/// Never includes secrets.
/// </summary>
public static class ReportService
{
    public static readonly string[] SupportedFormats = ["txt", "json", "html"];

    public static string SaveProfile(
        IntelligenceProfile profile, string format, string projectRoot)
    {
        string normalized = (format ?? "").Trim().ToLowerInvariant();
        if (!SupportedFormats.Contains(normalized, StringComparer.Ordinal))
        {
            throw new UsageException(
                $"Unsupported report format: '{format}'. Supported: txt, json, html.");
        }

        string date = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        string fileName = $"iptracex-{SafeName(profile.Target)}-{stamp}.{normalized}";
        string outDir = Path.Combine(projectRoot, "reports", date);
        Directory.CreateDirectory(outDir);
        string resolved = SafeResolve(outDir, fileName);

        string content = normalized switch
        {
            "json" => GeoJson.ProfileToJsonString(profile, indented: true) + "\n",
            "html" => ToHtml(profile),
            _ => ToText(profile),
        };
        File.WriteAllText(resolved, content, System.Text.Encoding.UTF8);
        return resolved;
    }

    public static string SafeName(string target)
    {
        string safe = Regex.Replace(target.Trim(), @"[^A-Za-z0-9_.-]", "_");
        safe = safe.Replace(":", "_");
        if (safe.Length > 64)
        {
            safe = safe[..64];
        }

        if (safe is "." or ".." or "")
        {
            safe = "report";
        }

        return safe;
    }

    internal static string SafeResolve(string outDir, string fileName)
    {
        string fullOut = Path.GetFullPath(outDir);
        string resolved = Path.GetFullPath(Path.Combine(fullOut, Path.GetFileName(fileName)));
        if (!resolved.StartsWith(fullOut + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(resolved, fullOut, StringComparison.Ordinal))
        {
            throw new UsageException("Unsafe output filename.");
        }

        return resolved;
    }

    public static string ToText(IntelligenceProfile profile)
    {
        var g = profile.Geo.Geolocation;
        var n = profile.Geo.Network;
        var lines = new List<string>
        {
            "IPTraceX Investigation Report",
            $"Tool version : {profile.Metadata.ToolVersion}",
            $"Timestamp    : {profile.Metadata.TimestampUtc:O}",
            $"Target       : {profile.Target} (IPv{profile.Geo.IpVersion})",
            "",
            "[1] LOCATION",
            $"    Country      : {Text(g.Country)}",
            $"    Country Code : {Text(g.CountryCode)}",
            $"    Region       : {Text(g.Region)}",
            $"    City         : {Text(g.City)}",
            $"    Latitude     : {Text(g.Latitude)}",
            $"    Longitude    : {Text(g.Longitude)}",
            $"    Timezone     : {Text(g.Timezone)}",
            "",
            "[2] NETWORK",
            $"    ISP          : {Text(n.Isp)}",
            $"    Organization : {Text(n.Organization)}",
            $"    ASN          : {Text(n.Asn)}",
            $"    Hostname     : {Text(n.Hostname)}",
            "",
            "[3] ASN INTELLIGENCE",
            $"    Prefix       : {Text(profile.Asn.Prefix)}",
            $"    Holder       : {Text(profile.Asn.Holder)}",
            $"    Registry     : {Text(profile.Asn.Registry)}",
            "",
            "[4] DNS",
            $"    PTR          : {(profile.Dns.PtrHostnames.Length == 0 ? "none observed" : string.Join(", ", profile.Dns.PtrHostnames))}",
            $"    Confidence   : {profile.Dns.Confidence}",
            "",
            "[5] ANONYMITY",
            $"    Tor          : {profile.Anonymity.Tor.Status} ({profile.Anonymity.Tor.Confidence})",
            $"    VPN          : {profile.Anonymity.Vpn.Status} ({profile.Anonymity.Vpn.Confidence})",
            $"    Proxy        : {profile.Anonymity.Proxy.Status} ({profile.Anonymity.Proxy.Confidence})",
            $"    Hosting      : {profile.Anonymity.Hosting.Status} ({profile.Anonymity.Hosting.Confidence})",
            "",
            "[6] RISK",
            $"    Score        : {(profile.Risk.Score.HasValue ? profile.Risk.Score + "/100" : "unknown")}",
            $"    Level        : {profile.Risk.Level}",
        };
        foreach (RiskEvidence e in profile.Risk.Evidence)
        {
            lines.Add($"    +{e.Weight} {e.Indicator} [{e.Severity}] ({e.Source}): {e.Evidence}");
        }

        lines.Add("");
        lines.Add("[7] PROVIDERS");
        foreach (ProviderOutcome outcome in profile.Providers)
        {
            lines.Add($"    {outcome.ProviderId,-14} : {outcome.Status}"
                + (outcome.Error is not null ? $" ({outcome.Error})" : ""));
        }

        lines.Add("");
        lines.Add("NOTE: IP geolocation is approximate and may identify the ISP's");
        lines.Add("registered location rather than a physical user location.");
        lines.Add("");
        return string.Join("\n", lines);
    }

    internal static string ToHtml(IntelligenceProfile profile)
    {
        static string E(string? value) => System.Net.WebUtility.HtmlEncode(value ?? "Unknown");
        var rows = new List<string>();
        void Section(string title) => rows.Add($"<h2>{E(title)}</h2>");
        void Row(string label, string? value) =>
            rows.Add($"<tr><th>{E(label)}</th><td>{E(value)}</td></tr>");
        void Table(Action body)
        {
            rows.Add("<table>");
            body();
            rows.Add("</table>");
        }

        var g = profile.Geo.Geolocation;
        var n = profile.Geo.Network;
        Section("Target");
        Table(() =>
        {
            Row("Target", profile.Target);
            Row("IP version", "IPv" + profile.Geo.IpVersion);
        });
        Section("Location");
        Table(() =>
        {
            Row("Country", g.Country);
            Row("Country code", g.CountryCode);
            Row("Region", g.Region);
            Row("City", g.City);
            Row("Latitude", g.Latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Row("Longitude", g.Longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Row("Timezone", g.Timezone);
        });
        Section("Network");
        Table(() =>
        {
            Row("ISP", n.Isp);
            Row("Organization", n.Organization);
            Row("ASN", n.Asn);
            Row("Hostname", n.Hostname);
            Row("Prefix", profile.Asn.Prefix);
            Row("Registry", profile.Asn.Registry);
        });
        Section("DNS");
        Table(() =>
        {
            Row("PTR", profile.Dns.PtrHostnames.Length == 0
                ? "none observed" : string.Join(", ", profile.Dns.PtrHostnames));
            Row("Confidence", profile.Dns.Confidence);
        });
        Section("Anonymity");
        Table(() =>
        {
            Row("Tor", $"{profile.Anonymity.Tor.Status} ({profile.Anonymity.Tor.Confidence})");
            Row("VPN", $"{profile.Anonymity.Vpn.Status} ({profile.Anonymity.Vpn.Confidence})");
            Row("Proxy", $"{profile.Anonymity.Proxy.Status} ({profile.Anonymity.Proxy.Confidence})");
            Row("Hosting", $"{profile.Anonymity.Hosting.Status} ({profile.Anonymity.Hosting.Confidence})");
        });
        Section("Risk");
        Table(() =>
        {
            Row("Score", profile.Risk.Score.HasValue ? $"{profile.Risk.Score}/100" : "unknown");
            Row("Level", profile.Risk.Level);
            foreach (RiskEvidence e in profile.Risk.Evidence)
            {
                Row($"+{e.Weight} {e.Indicator}", $"{e.Severity} ({e.Source}): {e.Evidence}");
            }
        });
        Section("Providers");
        Table(() =>
        {
            foreach (ProviderOutcome outcome in profile.Providers)
            {
                Row(outcome.ProviderId, outcome.Status + (outcome.Error is null ? "" : $" ({outcome.Error})"));
            }
        });

        return "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n"
            + "<title>IPTraceX Investigation Report</title>\n<style>\n"
            + "body{background:#0d1117;color:#e6edf3;font-family:monospace;max-width:900px;margin:2em auto;padding:0 1em}\n"
            + "h1{color:#f0b429}h2{color:#f0b429;border-bottom:1px solid #30363d}\n"
            + "table{border-collapse:collapse;width:100%;margin-bottom:1em}\n"
            + "th,td{border:1px solid #30363d;padding:.4em .6em;text-align:left}\n"
            + "th{color:#79c0ff}\n"
            + ".note{color:#8b949e}\n"
            + "</style>\n</head>\n<body>\n<h1>IPTraceX Investigation Report</h1>\n"
            + $"<p>Target: {E(profile.Target)} · {E(profile.Metadata.TimestampUtc.ToString("O"))} · IPTraceX {E(profile.Metadata.ToolVersion)}</p>\n"
            + string.Join("\n", rows)
            + "\n<p class=\"note\">IP geolocation is approximate and may identify the ISP's "
            + "registered location rather than a physical user location.</p>\n</body>\n</html>\n";
    }

    private static string Text(object? value)
    {
        string text = value switch
        {
            null => "Unknown",
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString()?.Trim() ?? "",
        };
        if (text.Length == 0)
        {
            return "Unknown";
        }

        // Neutralize newline injection from untrusted provider data.
        return text.Replace('\r', ' ').Replace('\n', ' ');
    }
}
