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
            + $"{p.TokenInfo()} Developer: t1_haaa\n"
            + $"{p.TokenOk()} Status: Ready\n";

    /// <summary>
    /// The launcher prints this same header while bootstrapping; when it
    /// already did (IPTraceX_LAUNCHER_UI=1), the app skips it to avoid a
    /// duplicated logo. Direct binary runs always show it.
    /// </summary>
    public static bool StartupShownByLauncher()
        => Environment.GetEnvironmentVariable("IPTraceX_LAUNCHER_UI") == "1";

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

    public static string FormatProfile(IntelligenceProfile profile, Palette p)
    {
        var lines = new List<string>
        {
            "",
            $"{p.TokenOk()} {p.Brand("TARGET")}",
            $"    Target       : {p.Data(profile.Target)}",
            $"    Type         : {p.Data(profile.IsDomainTarget ? "Domain" : profile.Geo.IpVersion == 6 ? "IPv6" : "IPv4")}",
            "",
        };
        // Reuse the classic geo/network/maps/quality rendering verbatim.
        lines.Add(FormatReport(profile.Geo, p).Trim('\n'));
        lines.Add("");
        var asn = profile.Asn;
        lines.Add($"{p.TokenOk()} {p.Brand("ASN INTELLIGENCE")}");
        lines.Add($"    ASN          : {p.Data(V(asn.Asn))}");
        lines.Add($"    Organization : {p.Data(V(asn.Organization))}");
        lines.Add($"    Network Name : {p.Data(V(asn.NetworkName))}");
        lines.Add($"    Prefix       : {p.Data(V(asn.Prefix))}");
        lines.Add($"    Registry     : {p.Data(V(asn.Registry))}");
        lines.Add($"    Country      : {p.Data(V(asn.Country))}");
        lines.Add("");
        var dns = profile.Dns;
        lines.Add($"{p.TokenOk()} {p.Brand("DNS INTELLIGENCE")}");
        lines.Add($"    PTR          : {p.Data(dns.PtrHostnames.Length == 0 ? "none observed" : string.Join(", ", dns.PtrHostnames))}");
        lines.Add($"    Confidence   : {p.Data(dns.Confidence)}");
        lines.Add("");
        var anon = profile.Anonymity;
        lines.Add($"{p.TokenOk()} {p.Brand("ANONYMITY")}");
        lines.Add($"    Tor          : {p.Data(Show(anon.Tor))} ({anon.Tor.Confidence})");
        lines.Add($"    VPN          : {p.Data(Show(anon.Vpn))} ({anon.Vpn.Confidence})");
        lines.Add($"    Proxy        : {p.Data(Show(anon.Proxy))} ({anon.Proxy.Confidence})");
        lines.Add($"    Hosting      : {p.Data(Show(anon.Hosting))} ({anon.Hosting.Confidence})");
        lines.Add("");
        var risk = profile.Risk;
        lines.Add($"{p.TokenOk()} {p.Brand("RISK ASSESSMENT")}");
        lines.Add($"    Score        : {p.Data(risk.Score.HasValue ? $"{risk.Score}/100" : "unknown")}");
        lines.Add($"    Level        : {p.Data(risk.Level)}");
        foreach (RiskEvidence e in risk.Evidence)
        {
            lines.Add($"    +{e.Weight,-3} {p.Data(e.Indicator)} [{e.Severity}] ({e.Source})");
            lines.Add($"         {e.Evidence}");
        }

        lines.Add("");
        lines.Add($"{p.TokenOk()} {p.Brand("FIELD CONFIDENCE")}");
        foreach (FieldConfidence field in profile.FieldConfidences)
        {
            lines.Add($"    {field.Field,-12} : {p.Data(field.Value ?? "Unknown")} ({field.Confidence.ToUpperInvariant()}, {field.Agreeing}/{field.Successful})");
        }

        lines.Add("");
        return string.Join("\n", lines);
    }

    private static string Show(AnonymitySignal signal)
        => signal.Status switch
        {
            DetectionStatus.Detected => "DETECTED",
            DetectionStatus.NotDetected => "NOT DETECTED",
            _ => "UNKNOWN",
        };

    public static string FormatProviders(
        Palette p,
        IReadOnlyList<ProviderDescriptor> descriptors,
        IReadOnlyDictionary<string, string> health)
    {
        var lines = new List<string>
        {
            "",
            $"{p.TokenOk()} {p.Brand("PROVIDER HEALTH")}",
            "",
        };
        foreach (ProviderDescriptor descriptor in descriptors)
        {
            string state = health.TryGetValue(descriptor.Id, out string? value)
                ? value
                : "UNKNOWN (not queried yet)";
            lines.Add($"    {p.Data(descriptor.Id.PadRight(14))} : {state}");
            lines.Add($"      {descriptor.DisplayName} [{descriptor.Category}] "
                + $"IPv{string.Join("/IPv", descriptor.SupportedIpVersions)}"
                + (descriptor.RequiresKey ? " (key required)" : " (no key)"));
        }

        lines.Add("");
        return string.Join("\n", lines);
    }

    public static string MainMenu(Palette p)
        => string.Join("\n", new[]
        {
            "",
            $"{p.TokenInfo()} Main menu",
            "",
            "[01] Analyze IP",
            "[02] Analyze domain",
            "[03] Analyze email",
            "[04] Self IP intelligence",
            "[05] Reverse DNS",
            "[06] Provider status",
            "[07] Batch analysis from file",
            "[08] Investigations",
            "[09] Reports",
            "[10] Configuration",
            "[00] Exit",
            "",
            $"{p.TokenAsk()} Select an option:",
        });

    public static string InvestigationsMenu(Palette p)
        => string.Join("\n", new[]
        {
            "",
            $"{p.TokenInfo()} Investigations",
            "",
            "[01] New investigation",
            "[02] Open investigation",
            "[03] List investigations",
            "[04] Compare investigations",
            "[05] Generate report",
            "[06] Delete investigation",
            "[00] Back",
            "",
            $"{p.TokenAsk()} Select an option:",
        });

    public static string FormatInvestigation(Investigation investigation, Palette p)
    {
        if (investigation.Email is not null && investigation.TargetType == "email")
        {
            return FormatEmailInvestigation(investigation, p);
        }

        if (investigation.Profile is null)
        {
            return $"\n{p.TokenError()} Investigation data unavailable.\n";
        }

        var lines = new List<string>
        {
            "",
            $"{p.TokenOk()} {p.Brand("INVESTIGATION")}",
            $"    ID           : {p.Data(investigation.Id)}",
            $"    Target       : {p.Data(investigation.Target)}",
            $"    Timestamp    : {p.Data(investigation.TimestampUtc.ToString("yyyy-MM-dd HH:mm"))} UTC",
            $"    Tool         : {p.Data($"IPTraceX {investigation.ToolVersion}")}",
            "",
        };
        lines.Add(FormatProfile(investigation.Profile, p).Trim('\n'));
        lines.Add("");
        lines.Add(FormatEvidenceMatrix(investigation.Profile, p).Trim('\n'));
        lines.Add("");
        return string.Join("\n", lines);
    }

    public static string FormatEmailInvestigation(Investigation investigation, Palette p)
    {
        var lines = new List<string>
        {
            "",
            $"{p.TokenOk()} {p.Brand("EMAIL INVESTIGATION")}",
            $"    ID           : {p.Data(investigation.Id)}",
            $"    Target       : {p.Data(investigation.Target)}",
            $"    Timestamp    : {p.Data(investigation.TimestampUtc.ToString("yyyy-MM-dd HH:mm"))} UTC",
            $"    Tool         : {p.Data($"IPTraceX {investigation.ToolVersion}")}",
            "",
        };
        if (investigation.Email is not null)
        {
            lines.Add(FormatStoredEmail(investigation.Email, investigation.Target, p).Trim('\n'));
            lines.Add("");
        }

        return string.Join("\n", lines);
    }

    private static string FormatStoredEmail(
        System.Text.Json.Nodes.JsonObject email, string target, Palette p)
    {
        string Str(string key) => email[key]?.GetValue<string?>() ?? "Unknown";
        return string.Join("\n", new[]
        {
            $"{p.TokenOk()} {p.Brand("EMAIL INTELLIGENCE")}",
            $"    Target       : {p.Data(target)}",
            $"    Domain       : {p.Data(Str("domain"))}",
            "",
        });
    }

    /// <summary>Human-readable email profile from live analysis.</summary>
    public static string FormatEmailProfile(EmailProfile profile, Palette p)
    {
        var lines = new List<string>
        {
            "",
            $"{p.TokenOk()} {p.Brand("EMAIL INTELLIGENCE")}",
            "",
            $"    Target       : {p.Data(profile.Target)}",
            $"    Domain       : {p.Data(profile.Domain)}",
            "",
            $"{p.TokenOk()} {p.Brand("DOMAIN INTELLIGENCE")}",
            $"    MX           : {p.Data(profile.DomainIntel.MxHosts.Length == 0 ? "none" : string.Join(", ", profile.DomainIntel.MxHosts))}",
            $"    Mail Provider: {p.Data(profile.DomainIntel.MailProvider ?? "Unknown")}",
            $"    SPF          : {p.Data(profile.DomainIntel.SpfRecord is null ? "NOT FOUND" : "FOUND")}",
            $"    DMARC        : {p.Data(profile.DomainIntel.DmarcRecord is null ? "NOT FOUND" : "FOUND")}",
            $"    DNSSEC       : {p.Data(profile.DomainIntel.DnssecStatus)}",
            $"    Disposable   : {p.Data(profile.DomainIntel.DisposableStatus)}",
            $"    Registrar    : {p.Data(profile.DomainIntel.Registrar ?? "Unknown")}",
            "",
            $"{p.TokenOk()} {p.Brand("PUBLIC AVATAR")}",
            $"    Status       : {p.Data(profile.Avatar.Status)}",
        };
        if (profile.Avatar.Url is not null)
        {
            lines.Add($"    URL          : {p.Data(profile.Avatar.Url)}");
        }

        lines.Add("");
        lines.Add($"{p.TokenOk()} {p.Brand("PUBLIC FOOTPRINT")}");
        if (profile.Footprint.Count == 0)
        {
            lines.Add("    No public matches observed.");
        }

        foreach (FootprintMatch match in profile.Footprint)
        {
            lines.Add($"    [{match.Platform}] {p.Data(match.Url)}");
            lines.Add($"      {match.EvidenceType}: {match.MatchedValue} ({match.Confidence})");
        }

        lines.Add("");
        lines.Add($"{p.TokenOk()} {p.Brand("BREACH INTELLIGENCE")}");
        if (profile.Breaches.Count == 0)
        {
            lines.Add("    No breach indicators (or no breach source configured).");
        }

        foreach (BreachInfo breach in profile.Breaches)
        {
            lines.Add($"    {p.Data(breach.Name)}"
                + (breach.Date is null ? "" : $" ({breach.Date})")
                + (breach.Domain is null ? "" : $" [{breach.Domain}]"));
            if (breach.Categories.Length != 0)
            {
                lines.Add($"      Categories: {string.Join(", ", breach.Categories)}");
            }
        }

        lines.Add("");
        lines.Add($"{p.TokenOk()} {p.Brand("EMAIL RISK")}");
        lines.Add($"    Score        : {p.Data(profile.Risk.Score.HasValue ? $"{profile.Risk.Score}/100" : "unknown")}");
        lines.Add($"    Level        : {p.Data(profile.Risk.Level)}");
        foreach (RiskEvidence e in profile.Risk.Evidence)
        {
            lines.Add($"    +{e.Weight,-3} {p.Data(e.Indicator)} [{e.Severity}] ({e.Source})");
        }

        lines.Add("");
        lines.Add($"{p.TokenOk()} {p.Brand("EMAIL CONFIDENCE")}");
        foreach (FieldConfidence field in profile.FieldConfidences)
        {
            lines.Add($"    {field.Field,-12} : {p.Data(field.Value ?? "Unknown")} ({field.Confidence.ToUpperInvariant()})");
            lines.Add($"                   {field.Reason}");
        }

        lines.Add("");
        lines.Add($"{p.TokenOk()} {p.Brand("EMAIL PROVIDERS")}");
        foreach (ProviderOutcome outcome in profile.Providers)
        {
            lines.Add($"    {p.Data(outcome.ProviderId.PadRight(14))} : {outcome.Status}"
                + (outcome.Error is null ? "" : $" ({outcome.Error})"));
        }

        lines.Add("");
        lines.Add(p.TokenWarn() + " NOTE");
        lines.Add("    Email intelligence is approximate and limited to public sources.");
        lines.Add("    Weak correlations are labeled as such, never as identity.");
        lines.Add("");
        return string.Join("\n", lines);
    }

    public static string FormatEvidenceMatrix(IntelligenceProfile profile, Palette p)
    {
        var lines = new List<string>
        {
            "",
            $"{p.TokenOk()} {p.Brand("EVIDENCE MATRIX")}",
            "",
            "    Field       Provider       Value                     Status",
            "    ------------------------------------------------------------",
        };
        foreach (EvidenceRow row in Evidence.BuildMatrix(profile))
        {
            lines.Add($"    {row.Field.PadRight(11)} {row.Provider.PadRight(14)} "
                + $"{Truncate(row.Value, 25).PadRight(25)} {row.Status}");
        }

        lines.Add("");
        lines.Add($"{p.TokenInfo()} Consensus:");
        lines.Add($"    Country {profile.Geo.ProvidersAgreeing}/{profile.Geo.ProvidersSuccessful}");
        lines.Add($"{p.TokenInfo()} Confidence:");
        foreach (FieldConfidence field in profile.FieldConfidences)
        {
            lines.Add($"    {field.Field} {field.Confidence.ToUpperInvariant()} "
                + $"({field.Agreeing}/{field.Successful}) - {field.Reason}");
        }

        lines.Add("");
        return string.Join("\n", lines);
    }

    public static string FormatTimeline(IReadOnlyList<Investigation> history, Palette p)
    {
        var ordered = history.OrderBy(i => i.TimestampUtc).ToList();
        var lines = new List<string>
        {
            "",
            $"{p.TokenOk()} {p.Brand("INTELLIGENCE TIMELINE")}",
            "",
        };
        foreach (Investigation item in ordered)
        {
            lines.Add($"    {item.TimestampUtc:yyyy-MM-dd}  {item.Id}");
            if (item.Profile is not null)
            {
                var snap = Evidence.Snapshot(item.Profile);
                lines.Add($"      ASN: {snap["ASN"] ?? "Unknown"}  "
                    + $"Risk: {snap["Risk"] ?? "unknown"}  Tor: {snap["Tor"] ?? "UNKNOWN"}");
            }
            else if (item.Email is not null)
            {
                var snap = Evidence.EmailSnapshotFromJson(item.Email);
                lines.Add($"      Domain: {snap["Domain"] ?? "Unknown"}  "
                    + $"Risk: {snap["Risk"] ?? "unknown"}  Breaches: {snap["Breaches"] ?? "0"}");
            }
        }

        List<string> changes = Evidence.TimelineChanges(ordered);
        lines.Add("");
        lines.Add($"{p.TokenInfo()} CHANGES");
        lines.Add("    ------------------------------------------------------------");
        if (changes.Count == 0)
        {
            lines.Add("    No field changes across this history.");
        }
        else
        {
            foreach (string change in changes)
            {
                lines.Add($"    {change}");
            }
        }

        lines.Add("");
        lines.Add($"{p.TokenWarn()} NOTE");
        lines.Add("    A change in GeoIP result does NOT prove physical movement.");
        lines.Add("    It may reflect database updates, provider changes,");
        lines.Add("    reassignment, BGP changes, or ISP data corrections.");
        lines.Add("");
        return string.Join("\n", lines);
    }

    public static string FormatComparison(
        Investigation first, Investigation second, Palette p)
    {
        var lines = new List<string>
        {
            "",
            $"{p.TokenOk()} {p.Brand("INVESTIGATION COMPARISON")}",
            "",
            $"    A: {first.Id} ({first.TimestampUtc:yyyy-MM-dd})",
            $"    B: {second.Id} ({second.TimestampUtc:yyyy-MM-dd})",
            "",
            "    Field           A                   B",
            "    ------------------------------------------------------------",
        };
        int changed = 0;
        foreach (var (field, a, b, isChanged) in Evidence.Compare(first, second))
        {
            if (isChanged)
            {
                changed++;
            }

            lines.Add($"    {field.PadRight(15)} {(a ?? "-").PadRight(19)} {(b ?? "-").PadRight(19)}"
                + (isChanged ? "  CHANGE DETECTED" : ""));
        }

        lines.Add("");
        lines.Add($"{p.TokenInfo()} Changed fields: {changed}");
        lines.Add("");
        return string.Join("\n", lines);
    }

    public static string FormatProfileComparison(
        IntelligenceProfile first, IntelligenceProfile second, Palette p)
    {
        var lines = new List<string>
        {
            "",
            $"{p.TokenOk()} {p.Brand("IP COMPARISON")}",
            "",
            $"    A: {first.Target}",
            $"    B: {second.Target}",
            "",
            "    Field           A                   B",
            "    ------------------------------------------------------------",
        };
        int changed = 0;
        foreach (var (field, a, b, isChanged) in Evidence.CompareProfiles(first, second))
        {
            if (isChanged)
            {
                changed++;
            }

            lines.Add($"    {field.PadRight(15)} {(a ?? "-").PadRight(19)} {(b ?? "-").PadRight(19)}"
                + (isChanged ? "  CHANGE DETECTED" : ""));
        }

        lines.Add("");
        lines.Add($"{p.TokenInfo()} Changed fields: {changed}");
        lines.Add("");
        return string.Join("\n", lines);
    }

    public static string FormatEmailEvidence(EmailProfile profile, Palette p)
    {
        var lines = new List<string>
        {
            "",
            $"{p.TokenOk()} {p.Brand("EMAIL EVIDENCE")}",
            "",
            "    Field        Value                     Status",
            "    ------------------------------------------------------------",
            $"    {"domain",-12} {Truncate(profile.Domain, 25).PadRight(25)} SUPPORT",
            $"    {"mail-provider",-12} {Truncate(profile.DomainIntel.MailProvider ?? "Unknown", 25).PadRight(25)} {(profile.DomainIntel.MailProvider is null ? "MISSING" : "SUPPORT")}",
            $"    {"disposable",-12} {Truncate(profile.DomainIntel.DisposableStatus, 25).PadRight(25)} {(profile.DomainIntel.DisposableStatus == "UNKNOWN" ? "MISSING" : "SUPPORT")}",
            $"    {"avatar",-12} {Truncate(profile.Avatar.Status, 25).PadRight(25)} {(profile.Avatar.Status == "UNKNOWN" ? "MISSING" : "SUPPORT")}",
            $"    {"breaches",-12} {Truncate(profile.Breaches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), 25).PadRight(25)} SUPPORT",
            "",
        };
        return string.Join("\n", lines);
    }

    public static string FormatEmailDomain(EmailProfile profile, Palette p)
    {
        var d = profile.DomainIntel;
        return string.Join("\n", new[]
        {
            "",
            $"{p.TokenOk()} {p.Brand("EMAIL DOMAIN INTELLIGENCE")}",
            "",
            $"    Domain       : {p.Data(profile.Domain)}",
            $"    MX           : {p.Data(d.MxHosts.Length == 0 ? "none" : string.Join(", ", d.MxHosts))}",
            $"    Mail Provider: {p.Data(d.MailProvider ?? "Unknown")}",
            $"    SPF          : {p.Data(d.SpfRecord is null ? "NOT FOUND" : "FOUND")}",
            $"    DMARC        : {p.Data(d.DmarcRecord is null ? "NOT FOUND" : "FOUND")}",
            $"    DNSSEC       : {p.Data(d.DnssecStatus)}",
            $"    Disposable   : {p.Data(d.DisposableStatus)}",
            $"    Free mail    : {p.Data(d.IsFreeMail ? "YES" : "NO")}",
            $"    Registrar    : {p.Data(d.Registrar ?? "Unknown")}",
            "",
        });
    }

    public static string FormatEmailSummary(EmailProfile profile, Palette p)
        => $"{p.TokenOk()} {profile.Target} | "
            + $"{profile.DomainIntel.MailProvider ?? "unknown provider"} | "
            + $"risk {profile.Risk.Score?.ToString() ?? "unknown"}/{profile.Risk.Level}";

    private static string Truncate(string value, int width)
        => value.Length <= width ? value : value[..(width - 3)] + "...";
}
