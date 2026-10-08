using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure;

/// <summary>Storage seam for investigations (file-backed by default).</summary>
public interface IInvestigationStore
{
    void Save(Investigation investigation);
    Investigation Get(string id);
    IReadOnlyList<InvestigationSummary> List();
    bool Exists(string id);
    void Delete(string id);
    IReadOnlyList<Investigation> HistoryFor(string target);
}

/// <summary>Lightweight listing row (no full profile load).</summary>
public sealed record InvestigationSummary(
    string Id, DateTimeOffset TimestampUtc, string Target, string ToolVersion);

/// <summary>
/// File-backed investigation store: investigations/YYYY-MM-DD/IPX-*.json.
/// Atomic writes (temp + move), validated IDs only (no traversal),
/// UTF-8, schema-checked reads. Never stores secrets.
/// </summary>
public sealed class FileInvestigationStore : IInvestigationStore
{
    public const string CurrentSchema = Investigation.CurrentSchema;

    private static readonly Regex SafeId =
        new(@"^IPX-\d{8}-[A-Z0-9]{6}$", RegexOptions.Compiled);

    private readonly string _root;

    public FileInvestigationStore(string projectRoot)
    {
        _root = Path.GetFullPath(Path.Combine(projectRoot, "investigations"));
    }

    public void Save(Investigation investigation)
    {
        string id = InvestigationId.RequireValid(investigation.Id);
        string date = investigation.TimestampUtc.ToString("yyyy-MM-dd");
        string dir = Path.Combine(_root, date);
        Directory.CreateDirectory(dir);
        string path = SafePath(dir, id + ".json");

        string payload = Serialize(investigation);
        string temp = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(temp, payload, System.Text.Encoding.UTF8);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    public Investigation Get(string id)
    {
        string found = Find(id);
        JsonObject data;
        try
        {
            data = JsonNode.Parse(File.ReadAllText(found, System.Text.Encoding.UTF8))?.AsObject()
                ?? throw new BadResponseException("Investigation file is corrupt.");
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
            || ex is JsonException)
        {
            throw new BadResponseException($"Investigation file is corrupt: {id}.");
        }

        string? schema = data["schema_version"]?.GetValue<string?>();
        if (schema != CurrentSchema)
        {
            throw new BadResponseException(
                $"Unsupported investigation schema: '{schema ?? "missing"}' (expected {CurrentSchema}).");
        }

        try
        {
            return Deserialize(data);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException
            || ex is ArgumentException || ex is JsonException)
        {
            throw new BadResponseException($"Investigation file is corrupt: {id}.");
        }
    }

    public IReadOnlyList<InvestigationSummary> List()
    {
        var summaries = new List<InvestigationSummary>();
        if (!Directory.Exists(_root))
        {
            return summaries;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(_root, "IPX-*.json", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return summaries;
        }

        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!SafeId.IsMatch(name))
            {
                continue;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file, System.Text.Encoding.UTF8));
                var root = doc.RootElement;
                summaries.Add(new InvestigationSummary(
                    name,
                    root.TryGetProperty("timestamp_utc", out JsonElement ts)
                        && DateTimeOffset.TryParse(ts.GetString(), out DateTimeOffset parsed)
                        ? parsed : File.GetCreationTimeUtc(file),
                    root.TryGetProperty("target", out JsonElement target)
                        ? target.GetString() ?? "?" : "?",
                    root.TryGetProperty("tool_version", out JsonElement version)
                        ? version.GetString() ?? "?" : "?"));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                || ex is JsonException)
            {
                continue; // skip corrupt files when listing
            }
        }

        return summaries;
    }

    public bool Exists(string id)
    {
        if (!InvestigationId.IsValid(id))
        {
            return false;
        }

        try
        {
            Find(id);
            return true;
        }
        catch (TraceXException)
        {
            return false;
        }
    }

    public void Delete(string id)
    {
        string found = Find(InvestigationId.RequireValid(id));
        try
        {
            File.Delete(found);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            throw new UsageException($"Cannot delete investigation: {id}.");
        }
    }

    public IReadOnlyList<Investigation> HistoryFor(string target)
    {
        string clean = target.Trim().ToLowerInvariant();
        var history = new List<Investigation>();
        foreach (InvestigationSummary summary in List())
        {
            if (!summary.Target.Trim().Equals(clean, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                history.Add(Get(summary.Id));
            }
            catch (TraceXException)
            {
                continue;
            }
        }

        history.Sort((a, b) => a.TimestampUtc.CompareTo(b.TimestampUtc));
        return history;
    }

    private string Find(string id)
    {
        string valid = InvestigationId.RequireValid(id);
        if (!Directory.Exists(_root))
        {
            throw new NotFoundException($"Investigation not found: {valid}.");
        }

        string[] matches;
        try
        {
            matches = Directory.GetFiles(_root, valid + ".json", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            throw new NotFoundException($"Investigation not found: {valid}.");
        }

        if (matches.Length == 0)
        {
            throw new NotFoundException($"Investigation not found: {valid}.");
        }

        return matches[0];
    }

    private static string SafePath(string dir, string fileName)
    {
        string fullDir = Path.GetFullPath(dir);
        string resolved = Path.GetFullPath(Path.Combine(fullDir, Path.GetFileName(fileName)));
        if (!resolved.StartsWith(fullDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new UsageException("Unsafe investigation filename.");
        }

        return resolved;
    }

    internal static string Serialize(Investigation investigation)
    {
        var node = (JsonObject?)JsonNode.Parse(GeoJson.ProfileToJsonString(investigation.Profile));
        // Retain per-provider votes so stored investigations keep full evidence.
        var votes = new JsonArray();
        foreach (GeoResult vote in investigation.Profile.Geo.Votes)
        {
            JsonObject? voteNode = JsonNode.Parse(GeoJson.ToJsonString(vote))?.AsObject();
            if (voteNode is not null)
            {
                voteNode["source"] = vote.Source;
                votes.Add(voteNode);
            }
        }

        node!["geo_votes"] = votes;
        var doc = new JsonObject
        {
            ["schema_version"] = CurrentSchema,
            ["id"] = investigation.Id,
            ["timestamp_utc"] = investigation.TimestampUtc.ToString("O"),
            ["tool_version"] = investigation.ToolVersion,
            ["target"] = investigation.Target,
            ["target_type"] = investigation.TargetType,
            ["profile"] = node,
            ["errors"] = new JsonArray(investigation.Errors.Select(e => (JsonNode)JsonValue.Create(e)!).ToArray()),
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        return doc.ToJsonString(options) + "\n";
    }

    internal static Investigation Deserialize(JsonObject data)
    {
        string id = data["id"]?.GetValue<string>() ?? throw new BadResponseException("Missing id.");
        InvestigationId.RequireValid(id);
        JsonObject profile = data["profile"]?.AsObject()
            ?? throw new BadResponseException("Missing profile.");
        var errors = new List<string>();
        if (data["errors"] is JsonArray arr)
        {
            foreach (JsonNode? item in arr)
            {
                if (item?.GetValue<string>() is string text)
                {
                    errors.Add(text);
                }
            }
        }

        return new Investigation(
            id,
            DateTimeOffset.Parse(
                data["timestamp_utc"]?.GetValue<string>() ?? DateTimeOffset.UtcNow.ToString("O")),
            data["tool_version"]?.GetValue<string>() ?? "?",
            data["target"]?.GetValue<string>() ?? "?",
            data["target_type"]?.GetValue<string>() ?? "ip",
            RebuildProfile(profile),
            errors,
            data["schema_version"]?.GetValue<string>() ?? CurrentSchema);
    }

    // Rebuilds the profile subset the app needs (geo/network/dns/security/
    // risk/asn/confidence/providers/metadata). Unknown extra keys are kept
    // by the JSON layer but not required here.
    internal static IntelligenceProfile RebuildProfile(JsonObject profile)
    {
        var geo = new GeoResult
        {
            Ip = profile["ip"]?.GetValue<string>() ?? "",
            IpVersion = profile["ip_version"]?.GetValue<int>() ?? 4,
        };
        if (profile["geolocation"]?.AsObject() is JsonObject geoNode)
        {
            geo.Geolocation.Country = geoNode["country"]?.GetValue<string?>();
            geo.Geolocation.CountryCode = geoNode["country_code"]?.GetValue<string?>();
            geo.Geolocation.Region = geoNode["region"]?.GetValue<string?>();
            geo.Geolocation.RegionCode = geoNode["region_code"]?.GetValue<string?>();
            geo.Geolocation.City = geoNode["city"]?.GetValue<string?>();
            geo.Geolocation.PostalCode = geoNode["postal_code"]?.GetValue<string?>();
            geo.Geolocation.Latitude = geoNode["latitude"]?.GetValue<double?>();
            geo.Geolocation.Longitude = geoNode["longitude"]?.GetValue<double?>();
            geo.Geolocation.Timezone = geoNode["timezone"]?.GetValue<string?>();
        }

        if (profile["network"]?.AsObject() is JsonObject netNode)
        {
            geo.Network.Isp = netNode["isp"]?.GetValue<string?>();
            geo.Network.Organization = netNode["organization"]?.GetValue<string?>();
            geo.Network.Asn = netNode["asn"]?.GetValue<string?>();
            geo.Network.AsName = netNode["as_name"]?.GetValue<string?>();
            geo.Network.Hostname = netNode["hostname"]?.GetValue<string?>();
        }

        geo.GoogleMapsUrl = profile["google_maps_url"]?.GetValue<string?>();
        if (profile["geoip_quality"]?.AsObject() is JsonObject quality)
        {
            geo.Confidence = quality["confidence"]?.GetValue<string?>() ?? "unknown";
            geo.ProvidersQueried = quality["providers_queried"]?.GetValue<int>() ?? 0;
            geo.ProvidersSuccessful = quality["providers_successful"]?.GetValue<int>() ?? 0;
            geo.ProvidersAgreeing = quality["providers_agreeing"]?.GetValue<int>() ?? 0;
            geo.AgreementRatio = quality["agreement_ratio"]?.GetValue<double?>() ?? 0.0;
            geo.Disputed = quality["disputed"]?.GetValue<bool>() ?? false;
            geo.Source = quality["source"]?.GetValue<string?>() ?? "";
        }

        if (profile["providers"] is JsonArray providers)
        {
            foreach (JsonNode? item in providers)
            {
                if (item is not JsonObject detail)
                {
                    continue;
                }

                geo.Providers.Add(new ProviderDetail
                {
                    Name = detail["name"]?.GetValue<string?>() ?? "",
                    Status = detail["status"]?.GetValue<string?>() ?? "",
                    Error = detail["error"]?.GetValue<string?>(),
                    Summary = detail["summary"]?.GetValue<string?>() ?? "",
                });
            }
        }

        AsnIntelligence asn = AsnFrom(profile["asn"]?.AsObject());
        DnsIntelligence dns = DnsFrom(profile["dns"]?.AsObject(), geo.Ip);
        AnonymityIntelligence anonymity = AnonymityFrom(profile["security"]?.AsObject());
        RiskAssessment risk = RiskFrom(profile["risk"]?.AsObject());
        var confidences = new List<FieldConfidence>();
        if (profile["consensus"] is JsonArray consensus)
        {
            foreach (JsonNode? item in consensus)
            {
                if (item is not JsonObject field)
                {
                    continue;
                }

                confidences.Add(new FieldConfidence(
                    field["field"]?.GetValue<string?>() ?? "?",
                    field["value"]?.GetValue<string?>(),
                    field["confidence"]?.GetValue<string?>() ?? "unknown",
                    field["agreeing"]?.GetValue<int>() ?? 0,
                    field["successful"]?.GetValue<int>() ?? 0,
                    "",
                    field["supporting"]?.AsArray().Select(x => x?.GetValue<string?>() ?? "?").ToArray() ?? [],
                    field["conflicting"]?.AsArray().Select(x => x?.GetValue<string?>() ?? "?").ToArray() ?? []));
            }
        }

        // Legacy files predate FieldConfidence.reason/support lists.
        if (confidences.Count == 0 && profile["consensus"] is null)
        {
            confidences.AddRange(new[]
            {
                new FieldConfidence("country", geo.Geolocation.Country, geo.Confidence, geo.ProvidersAgreeing, geo.ProvidersSuccessful, "", [], []),
            });
        }

        InvestigationMetadata metadata = MetadataFrom(profile["metadata"]?.AsObject());
        var rebuilt = new IntelligenceProfile(
            profile["target"]?.GetValue<string?>() ?? geo.Ip,
            geo.IpVersion, false, geo, asn, dns, anonymity, risk,
            confidences, [], metadata);
        if (profile["geo_votes"] is JsonArray votes)
        {
            foreach (JsonNode? item in votes)
            {
                if (item is not JsonObject vote)
                {
                    continue;
                }

                try
                {
                    var single = new GeoResult
                    {
                        Ip = vote["ip"]?.GetValue<string?>() ?? "",
                        IpVersion = vote["ip_version"]?.GetValue<int>() ?? 4,
                        Source = vote["source"]?.GetValue<string?>() ?? "",
                    };
                    if (vote["geolocation"]?.AsObject() is JsonObject voteGeo)
                    {
                        single.Geolocation.Country = voteGeo["country"]?.GetValue<string?>();
                        single.Geolocation.CountryCode = voteGeo["country_code"]?.GetValue<string?>();
                        single.Geolocation.Region = voteGeo["region"]?.GetValue<string?>();
                        single.Geolocation.RegionCode = voteGeo["region_code"]?.GetValue<string?>();
                        single.Geolocation.City = voteGeo["city"]?.GetValue<string?>();
                        single.Geolocation.PostalCode = voteGeo["postal_code"]?.GetValue<string?>();
                        single.Geolocation.Latitude = voteGeo["latitude"]?.GetValue<double?>();
                        single.Geolocation.Longitude = voteGeo["longitude"]?.GetValue<double?>();
                        single.Geolocation.Timezone = voteGeo["timezone"]?.GetValue<string?>();
                    }

                    if (vote["network"]?.AsObject() is JsonObject voteNet)
                    {
                        single.Network.Isp = voteNet["isp"]?.GetValue<string?>();
                        single.Network.Organization = voteNet["organization"]?.GetValue<string?>();
                        single.Network.Asn = voteNet["asn"]?.GetValue<string?>();
                        single.Network.AsName = voteNet["as_name"]?.GetValue<string?>();
                        single.Network.Hostname = voteNet["hostname"]?.GetValue<string?>();
                    }

                    rebuilt.Geo.Votes.Add(single);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException)
                {
                    continue;
                }
            }
        }

        return rebuilt;
    }

    private static AsnIntelligence AsnFrom(JsonObject? node)
    {
        if (node is null)
        {
            return new AsnIntelligence(null, null, null, null, null, null, null, []);
        }

        string[] Sources(JsonNode? list) =>
            list is JsonArray arr
                ? arr.Select(x => x?.GetValue<string?>() ?? "?").ToArray()
                : [];
        return new AsnIntelligence(
            node["asn"]?.GetValue<string?>(),
            node["organization"]?.GetValue<string?>(),
            node["network_name"]?.GetValue<string?>(),
            node["prefix"]?.GetValue<string?>(),
            node["registry"]?.GetValue<string?>(),
            node["country"]?.GetValue<string?>(),
            node["holder"]?.GetValue<string?>(),
            Sources(node["sources"]));
    }

    private static DnsIntelligence DnsFrom(JsonObject? node, string ip)
    {
        if (node is null)
        {
            return new DnsIntelligence(ip, [], "UNKNOWN", []);
        }

        var hosts = node["ptr_hostnames"] is JsonArray arr
            ? arr.Select(x => x?.GetValue<string?>()).Where(s => s is not null).Cast<string>().ToArray()
            : [];
        var sources = node["sources"] is JsonArray src
            ? src.Select(x => x?.GetValue<string?>()).Where(s => s is not null).Cast<string>().ToArray()
            : [];
        return new DnsIntelligence(
            node["ip"]?.GetValue<string?>() ?? ip, hosts,
            node["confidence"]?.GetValue<string?>() ?? "UNKNOWN", sources);
    }

    private static AnonymityIntelligence AnonymityFrom(JsonObject? node)
    {
        AnonymitySignal Signal(string key)
        {
            if (node?[key]?.AsObject() is not JsonObject section)
            {
                return new AnonymitySignal(DetectionStatus.Unknown, "UNKNOWN", [], "no data");
            }

            _ = Enum.TryParse<DetectionStatus>(section["status"]?.GetValue<string?>() ?? "", true, out DetectionStatus status);
            var sources = section["sources"] is JsonArray arr
                ? arr.Select(x => x?.GetValue<string?>()).Where(s => s is not null).Cast<string>().ToArray()
                : [];
            return new AnonymitySignal(
                status,
                section["confidence"]?.GetValue<string?>() ?? "UNKNOWN",
                sources,
                section["evidence"]?.GetValue<string?>() ?? "");
        }

        return new AnonymityIntelligence(
            Signal("tor"), Signal("vpn"), Signal("proxy"), Signal("hosting"));
    }

    private static RiskAssessment RiskFrom(JsonObject? node)
    {
        if (node is null)
        {
            return new RiskAssessment(null, "UNKNOWN", [], false);
        }

        var evidence = new List<RiskEvidence>();
        if (node["evidence"] is JsonArray arr)
        {
            foreach (JsonNode? item in arr)
            {
                if (item is not JsonObject entry)
                {
                    continue;
                }

                evidence.Add(new RiskEvidence(
                    entry["indicator"]?.GetValue<string?>() ?? "?",
                    entry["severity"]?.GetValue<string?>() ?? "?",
                    entry["source"]?.GetValue<string?>() ?? "?",
                    entry["evidence"]?.GetValue<string?>() ?? "",
                    entry["weight"]?.GetValue<int>() ?? 0,
                    entry["explanation"]?.GetValue<string?>() ?? ""));
            }
        }

        return new RiskAssessment(
            node["score"]?.GetValue<int?>(),
            node["level"]?.GetValue<string?>() ?? "UNKNOWN",
            evidence,
            node["has_sufficient_evidence"]?.GetValue<bool>() ?? evidence.Count != 0);
    }

    private static InvestigationMetadata MetadataFrom(JsonObject? node)
    {
        if (node is null)
        {
            return new InvestigationMetadata("?", DateTimeOffset.UtcNow, 0, [], 0, false);
        }

        var queried = node["providers_queried"] is JsonArray arr
            ? arr.Select(x => x?.GetValue<string?>()).Where(s => s is not null).Cast<string>().ToArray()
            : [];
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        try
        {
            timestamp = DateTimeOffset.Parse(node["timestamp_utc"]?.GetValue<string?>() ?? "");
        }
        catch (FormatException)
        {
        }

        return new InvestigationMetadata(
            node["tool_version"]?.GetValue<string?>() ?? "?",
            timestamp,
            node["duration_ms"]?.GetValue<long>() ?? 0,
            queried,
            node["providers_successful"]?.GetValue<int>() ?? 0,
            node["has_cached_results"]?.GetValue<bool>() ?? false);
    }
}
