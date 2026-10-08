namespace IPTraceX.Core;

/// <summary>
/// Evidence matrix, explained confidence, timelines and comparisons.
/// Pure functions over investigations and votes — no I/O, fully testable.
/// Disagreement is always preserved; reliability never erases evidence.
/// </summary>
public static class Evidence
{
    private static string Show(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();

    /// <summary>
    /// Build the evidence matrix for the core triple plus ASN from the
    /// retained provider votes. Ties already resolved by consensus; rows
    /// show every source's position.
    /// </summary>
    public static List<EvidenceRow> BuildMatrix(IntelligenceProfile profile)
    {
        var rows = new List<EvidenceRow>();
        var votes = profile.Geo.Votes.Count == 0 ? new List<GeoResult> { profile.Geo } : profile.Geo.Votes;
        var final = profile.Geo.Geolocation;
        var finalAsn = profile.Geo.Network.Asn;
        var freshnessByProvider = profile.Geo.Providers
            .ToDictionary(d => d.Name, d => d.Freshness, StringComparer.OrdinalIgnoreCase);

        foreach (GeoResult vote in votes)
        {
            string provider = string.IsNullOrEmpty(vote.Source) ? "unknown" : vote.Source;
            string freshness = freshnessByProvider.TryGetValue(provider, out string? freshnessValue)
                ? freshnessValue
                : "LIVE";
            rows.Add(Row("Country", provider, vote.Geolocation.Country, final.Country, freshness));
            rows.Add(Row("Region", provider, vote.Geolocation.Region, final.Region, freshness));
            rows.Add(Row("City", provider, vote.Geolocation.City, final.City, freshness));
            rows.Add(Row("ASN", provider, vote.Network.Asn, finalAsn, freshness));
        }

        return rows;
    }

    private static EvidenceRow Row(
        string field, string provider, string? value, string? finalValue, string freshness)
    {
        if (value is null)
        {
            return new EvidenceRow(field, provider, "Unknown", EvidenceStatus.Missing);
        }

        if (freshness == EvidenceStatus.Cached)
        {
            return new EvidenceRow(field, provider, Show(value), EvidenceStatus.Cached);
        }

        bool agrees = string.Equals(
            value.Trim(), (finalValue ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        return new EvidenceRow(
            field, provider, Show(value), agrees ? EvidenceStatus.Support : EvidenceStatus.Conflict);
    }

    /// <summary>
    /// Explained confidence per field: level + reason + supporters/conflicts.
    /// Mirrors the consensus ladder; reliability breaks documentation only.
    /// </summary>
    public static List<ConfidenceDetail> ExplainConfidence(IntelligenceProfile profile)
    {
        var votes = profile.Geo.Votes.Count == 0 ? new List<GeoResult> { profile.Geo } : profile.Geo.Votes;
        var final = profile.Geo.Geolocation;
        var result = new List<ConfidenceDetail>
        {
            Explain("country", final.Country, votes.Select(v => (v.Source, v.Geolocation.Country))),
            Explain("region", final.Region, votes.Select(v => (v.Source, v.Geolocation.Region))),
            Explain("city", final.City, votes.Select(v => (v.Source, v.Geolocation.City))),
            Explain("asn", profile.Geo.Network.Asn, votes.Select(v => (v.Source, v.Network.Asn))),
        };
        return result;
    }

    private static ConfidenceDetail Explain(
        string field, string? finalValue, IEnumerable<(string Source, string? Value)> votes)
    {
        var counted = votes.ToList();
        var supporters = counted
            .Where(v => Same(v.Value, finalValue) && v.Value is not null)
            .Select(v => v.Source ?? "unknown")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var conflicts = counted
            .Where(v => v.Value is not null && !Same(v.Value, finalValue))
            .Select(v => $"{v.Source ?? "unknown"}->{v.Value!.Trim()}")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        int total = counted.Count;
        int agreeing = supporters.Count;

        string level;
        string reason;
        if (finalValue is null)
        {
            level = "unknown";
            reason = "No provider returned this field.";
        }
        else if (total <= 1)
        {
            level = "medium";
            reason = "Single source, uncorroborated.";
        }
        else if (agreeing == total)
        {
            level = "high";
            reason = $"{agreeing} independent providers agree; no conflicting result.";
        }
        else if (agreeing * 2 >= total)
        {
            level = "medium";
            reason = $"{agreeing}/{total} providers agree; {conflicts.Count} conflicting result(s).";
        }
        else
        {
            level = "low";
            reason = $"Only {agreeing}/{total} providers agree; conflicting results dominate.";
        }

        return new ConfidenceDetail(
            field, finalValue, level, agreeing, total, reason,
            [.. supporters], [.. conflicts]);
    }

    private static bool Same(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tracked fields for timelines and comparisons.</summary>
    public static IReadOnlyDictionary<string, string?> Snapshot(IntelligenceProfile profile) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Country"] = profile.Geo.Geolocation.Country,
            ["Region"] = profile.Geo.Geolocation.Region,
            ["City"] = profile.Geo.Geolocation.City,
            ["ASN"] = profile.Geo.Network.Asn,
            ["ISP"] = profile.Geo.Network.Isp ?? profile.Geo.Network.Organization,
            ["Organization"] = profile.Geo.Network.Organization,
            ["Prefix"] = profile.Asn.Prefix,
            ["PTR"] = profile.Dns.PtrHostnames.Length == 0
                ? null : string.Join(", ", profile.Dns.PtrHostnames),
            ["VPN"] = profile.Anonymity.Vpn.Status.ToString().ToUpperInvariant(),
            ["Proxy"] = profile.Anonymity.Proxy.Status.ToString().ToUpperInvariant(),
            ["Tor"] = profile.Anonymity.Tor.Status.ToString().ToUpperInvariant(),
            ["Hosting"] = profile.Anonymity.Hosting.Status.ToString().ToUpperInvariant(),
            ["Risk"] = profile.Risk.Score?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Confidence"] = profile.Geo.Confidence,
        };

    /// <summary>Consecutive changes across a time-ordered history.</summary>
    public static List<string> TimelineChanges(IReadOnlyList<Investigation> history)
    {
        var changes = new List<string>();
        var ordered = history.OrderBy(i => i.TimestampUtc).ToList();
        for (int index = 1; index < ordered.Count; index++)
        {
            var before = Snapshot(ordered[index - 1].Profile);
            var after = Snapshot(ordered[index].Profile);
            foreach (var (field, _) in before)
            {
                if (!string.Equals(before[field], after[field], StringComparison.Ordinal))
                {
                    changes.Add($"{ordered[index].TimestampUtc:yyyy-MM-dd}: {field}: "
                        + $"{before[field] ?? "—"} → {after[field] ?? "—"}");
                }
            }
        }

        return changes;
    }

    /// <summary>Field-by-field comparison of two investigations.</summary>
    public static List<(string Field, string? A, string? B, bool Changed)> Compare(
        Investigation first, Investigation second)
    {
        var a = Snapshot(first.Profile);
        var b = Snapshot(second.Profile);
        return a.Keys
            .Select(field => (
                Field: field,
                A: a[field],
                B: b.TryGetValue(field, out string? value) ? value : null,
                Changed: !string.Equals(a[field], b.TryGetValue(field, out string? v) ? v : null,
                    StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>IP-to-IP comparison of two fresh profiles.</summary>
    public static List<(string Field, string? A, string? B, bool Changed)> CompareProfiles(
        IntelligenceProfile first, IntelligenceProfile second)
    {
        var a = Snapshot(first);
        var b = Snapshot(second);
        return a.Keys
            .Select(field => (
                Field: field,
                A: a[field],
                B: b.TryGetValue(field, out string? value) ? value : null,
                Changed: !string.Equals(a[field], b.TryGetValue(field, out string? v) ? v : null,
                    StringComparison.Ordinal)))
            .ToList();
    }
}
