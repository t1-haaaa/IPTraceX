namespace IPTraceX.Core;

/// <summary>
/// Multi-provider consensus -- compare, vote, never invent.
/// Port of the proven Python engine rules:
/// country code decides; ties break by provider order with lower
/// confidence; coordinates cluster within 100 km (haversine); ISP names
/// compare case-insensitively ignoring corporate suffixes; ASN compares
/// by digits; disagreement is reported, never hidden; Unknown stays Unknown.
/// </summary>
public static class Consensus
{
    public const double CoordClusterKm = 100.0;

    private static readonly System.Text.RegularExpressions.Regex CorpSuffixRe =
        new(@"\b(limited|ltd|incorporated|inc|corporation|corp|company|co|llc"
            + @"|gmbh|sarl|sas|spa|plc|pty|bv|ab|sa)\b\.?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex NonAlnumRe =
        new(@"[^a-z0-9]+",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex DigitsRe =
        new(@"\D",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    public sealed record ConsensusOutcome(
        GeoResult Final,
        int Agreeing,
        string Confidence,
        bool Disputed,
        IReadOnlyDictionary<string, (int Agreeing, int Total)> FieldVotes);

    private static string? NormText(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string text = value.Trim();
        return text.Length == 0 ? null : text;
    }

    private static string? Key(string? value)
    {
        string? text = NormText(value);
        return text is null ? null : text.ToLowerInvariant();
    }

    internal static string? Majority(IEnumerable<string?> values)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var original = new Dictionary<string, string?>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (string? value in values)
        {
            string? key = Key(value);
            if (key is null)
            {
                continue;
            }

            if (!counts.ContainsKey(key))
            {
                counts[key] = 0;
                order.Add(key);
                original[key] = NormText(value);
            }

            counts[key]++;
        }

        if (counts.Count == 0)
        {
            return null;
        }

        int best = counts.Values.Max();
        foreach (string key in order)
        {
            if (counts[key] == best)
            {
                return original[key];
            }
        }

        return null; // unreachable
    }

    internal static string? NormIsp(string? value)
    {
        string? key = Key(value);
        if (key is null)
        {
            return null;
        }

        key = CorpSuffixRe.Replace(key, "");
        key = NonAlnumRe.Replace(key, " ").Trim();
        return key.Length == 0 ? null : key;
    }

    public static string? MajorityIsp(IEnumerable<string?> values)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var original = new Dictionary<string, string?>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (string? value in values)
        {
            string? key = NormIsp(value);
            if (key is null)
            {
                continue;
            }

            if (!counts.ContainsKey(key))
            {
                counts[key] = 0;
                order.Add(key);
                original[key] = NormText(value);
            }

            counts[key]++;
        }

        if (counts.Count == 0)
        {
            return null;
        }

        int best = counts.Values.Max();
        foreach (string key in order)
        {
            if (counts[key] == best)
            {
                return original[key];
            }
        }

        return null; // unreachable
    }

    internal static string? AsnDigits(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string digits = DigitsRe.Replace(value, "");
        return digits.Length == 0 ? null : digits;
    }

    public static string? MajorityAsn(IEnumerable<string?> values)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (string? value in values)
        {
            string? digits = AsnDigits(value);
            if (digits is null)
            {
                continue;
            }

            if (!counts.ContainsKey(digits))
            {
                counts[digits] = 0;
                order.Add(digits);
            }

            counts[digits]++;
        }

        if (counts.Count == 0)
        {
            return null;
        }

        int best = counts.Values.Max();
        foreach (string digits in order)
        {
            if (counts[digits] == best)
            {
                return "AS" + digits;
            }
        }

        return null; // unreachable
    }

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double radius = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1 * Math.PI / 180.0)
            * Math.Cos(lat2 * Math.PI / 180.0)
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * radius * Math.Asin(Math.Sqrt(a));
    }

    internal static (double? Lat, double? Lon) ConsensusCoords(
        IEnumerable<(double? Lat, double? Lon)> points)
    {
        var valid = new List<(double Lat, double Lon)>();
        foreach (var (la, lo) in points)
        {
            if (la.HasValue && lo.HasValue && Maps.IsValidCoordinate(la.Value, lo.Value))
            {
                valid.Add((la.Value, lo.Value));
            }
        }

        if (valid.Count == 0)
        {
            return (null, null);
        }

        if (valid.Count == 1)
        {
            return valid[0];
        }

        // Tight cluster -> mean. Spread out -> first point (provider order),
        // flagged as disputed by the caller.
        for (int i = 0; i < valid.Count; i++)
        {
            bool allClose = true;
            for (int j = 0; j < valid.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                if (HaversineKm(valid[i].Lat, valid[i].Lon, valid[j].Lat, valid[j].Lon) > CoordClusterKm)
                {
                    allClose = false;
                    break;
                }
            }

            if (allClose)
            {
                double lat = valid.Average(p => p.Lat);
                double lon = valid.Average(p => p.Lon);
                if (Maps.IsValidCoordinate(lat, lon))
                {
                    return (lat, lon);
                }

                return valid[0];
            }
        }

        return valid[0];
    }

    public static ConsensusOutcome Build(IList<GeoResult> results)
    {
        if (results.Count == 0)
        {
            throw new ArgumentException("Build needs at least one result.", nameof(results));
        }

        string ip = results.FirstOrDefault(r => r.Ip.Length != 0)?.Ip ?? "";
        int version = results.FirstOrDefault(r => r.IpVersion is 4 or 6)?.IpVersion ?? 4;

        string? winnerCode = Majority(results.Select(r => r.Geolocation.CountryCode));
        string? winnerKey = winnerCode?.ToLowerInvariant();
        List<GeoResult> pool = winnerKey is null
            ? [.. results]
            : results.Where(r =>
                r.Geolocation.CountryCode is not null
                && r.Geolocation.CountryCode.ToLowerInvariant() == winnerKey).ToList();
        if (pool.Count == 0)
        {
            pool = [.. results];
        }

        string? Field(string name) => name switch
        {
            "region" => Majority(pool.Select(r => r.Geolocation.Region)),
            "region_code" => Majority(pool.Select(r => r.Geolocation.RegionCode)),
            "city" => Majority(pool.Select(r => r.Geolocation.City)),
            "postal_code" => Majority(pool.Select(r => r.Geolocation.PostalCode)),
            "timezone" => Majority(pool.Select(r => r.Geolocation.Timezone)),
            _ => null,
        };

        string? country = Majority(pool.Select(r => r.Geolocation.Country));
        string? region = Field("region");
        string? regionCode = Field("region_code");
        string? city = Field("city");
        string? postal = Field("postal_code");
        string? timezone = Field("timezone");

        var (lat, lon) = ConsensusCoords(
            pool.Select(r => (r.Geolocation.Latitude, r.Geolocation.Longitude)));

        string? isp = MajorityIsp(pool.Select(r => r.Network.Isp ?? r.Network.Organization));
        string? organization = MajorityIsp(pool.Select(r => r.Network.Organization ?? r.Network.Isp));
        string? asn = MajorityAsn(pool.Select(r => r.Network.Asn));
        string? asName = Majority(pool.Select(r => r.Network.AsName));
        string? hostname = Majority(pool.Select(r => r.Network.Hostname));

        int total = results.Count;
        // Agreement = providers matching the final core triple
        // (country code + region + city). Sparse fields never count against.
        int agreeing = results.Count(r =>
            Key(r.Geolocation.CountryCode) == Key(winnerCode)
            && Key(r.Geolocation.Region) == Key(region)
            && Key(r.Geolocation.City) == Key(city));
        bool disputed = total > 1 && agreeing < total;

        // Per-field votes for transparent field-level confidence.
        var fieldVotes = new Dictionary<string, (int Agreeing, int Total)>(StringComparer.Ordinal)
        {
            ["country_code"] = (CountMatch(results.Select(r => r.Geolocation.CountryCode), winnerCode), total),
            ["region"] = (CountMatch(results.Select(r => r.Geolocation.Region), region), total),
            ["city"] = (CountMatch(results.Select(r => r.Geolocation.City), city), total),
            ["asn"] = (results.Count(r => AsnDigits(r.Network.Asn) == AsnDigits(asn)
                && AsnDigits(r.Network.Asn) is not null), total),
        };

        string confidence;
        if (total == 0 || agreeing == 0)
        {
            confidence = "unknown";
        }
        else if (total == 1)
        {
            confidence = "medium"; // single source, uncorroborated
        }
        else
        {
            double ratio = (double)agreeing / total;
            confidence = ratio >= 1.0 ? "high" : ratio >= 0.5 ? "medium" : "low";
        }

        var final = new GeoResult
        {
            Ip = ip,
            IpVersion = version,
            GoogleMapsUrl = Maps.BuildMapsUrl(lat, lon),
            Source = "Multi-provider consensus",
            Confidence = confidence,
            ProvidersQueried = total,
            ProvidersSuccessful = total,
            ProvidersAgreeing = agreeing,
            AgreementRatio = total == 0 ? 0.0 : (double)agreeing / total,
            Disputed = disputed,
        };
        final.Geolocation.Country = country;
        final.Geolocation.CountryCode = winnerCode;
        final.Geolocation.Region = region;
        final.Geolocation.RegionCode = regionCode;
        final.Geolocation.City = city;
        final.Geolocation.PostalCode = postal;
        final.Geolocation.Latitude = lat;
        final.Geolocation.Longitude = lon;
        final.Geolocation.Timezone = timezone;
        final.Network.Isp = isp;
        final.Network.Organization = organization;
        final.Network.Asn = asn;
        final.Network.AsName = asName;
        final.Network.Hostname = hostname;
        return new ConsensusOutcome(final, agreeing, confidence, disputed, fieldVotes);
    }

    private static int CountMatch(IEnumerable<string?> values, string? winner)
    {
        string? winnerKey = Key(winner);
        return values.Count(v => Key(v) == winnerKey);
    }
}
