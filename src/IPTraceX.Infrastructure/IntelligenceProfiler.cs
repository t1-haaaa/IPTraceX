using System.Diagnostics;
using IPTraceX.Core;
using IPTraceX.Infrastructure.Providers;

namespace IPTraceX.Infrastructure;

/// <summary>
/// Builds complete intelligence profiles: geo consensus (existing engine)
/// plus ASN, DNS, anonymity, risk and per-field confidence. Additive —
/// the classic GeoResult path is untouched.
/// </summary>
public sealed class IntelligenceProfiler
{
    private readonly AppConfig _config;
    private readonly IGeoJsonFetcher? _fetcher;

    public IntelligenceProfiler(AppConfig config, IGeoJsonFetcher? fetcher = null)
    {
        _config = config;
        _fetcher = fetcher;
    }

    public async Task<IntelligenceProfile> AnalyzeIpAsync(
        string ipText,
        Action<string>? onStage = null,
        CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        bool sawCached = false;
        void Emit(string stage)
        {
            if (stage.StartsWith("cached:", StringComparison.Ordinal))
            {
                sawCached = true;
            }

            onStage?.Invoke(stage);
        }

        var geoEngine = new MultiProviderEngine(_config, _fetcher);
        GeoResult geo = await geoEngine.AnalyzeAsync(ipText, Emit, cancellationToken)
            .ConfigureAwait(false);

        var context = new ProviderContext(_config.TimeoutSeconds, _config.IpInfoToken, _config.AbuseIpDbKey);
        var orchestrator = new IntelOrchestrator(BuildIntelProviders());
        IReadOnlyList<IntelEvidence> intel = await orchestrator.RunAsync(
            geo.Ip, context, _config.MaxConcurrency,
            TimeSpan.FromSeconds(_config.GlobalTimeoutSeconds),
            s => Emit("intel:" + s), cancellationToken).ConfigureAwait(false);

        clock.Stop();
        return Assemble(ipText, geo, intel, orchestrator.Health, clock.ElapsedMilliseconds, sawCached);
    }

    public async Task<(DomainEvidence Resolution, List<IntelligenceProfile> Profiles)> AnalyzeDomainAsync(
        string domain,
        Action<string>? onStage = null,
        CancellationToken cancellationToken = default)
    {
        DomainEvidence resolution = await SystemDnsProvider.ResolveDomainAsync(domain, cancellationToken)
            .ConfigureAwait(false);
        var profiles = new List<IntelligenceProfile>();
        foreach (string ip in resolution.A.Concat(resolution.Aaaa))
        {
            cancellationToken.ThrowIfCancellationRequested();
            profiles.Add(await AnalyzeIpAsync(ip, onStage, cancellationToken).ConfigureAwait(false));
        }

        return (resolution, profiles);
    }

    public List<IIntelProvider> BuildIntelProviders()
    {
        var selected = new HashSet<string>(
            _config.IntelProviders.Select(s => s.Trim().ToLowerInvariant()),
            StringComparer.Ordinal);
        var providers = new List<IIntelProvider>();
        if (selected.Contains("ripestat"))
        {
            providers.Add(new RipeStatProvider(_config.TimeoutSeconds, _fetcher));
        }

        if (selected.Contains("doh-cloudflare"))
        {
            providers.Add(new CloudflareDohProvider(_config.TimeoutSeconds, _fetcher));
        }

        if (selected.Contains("doh-google"))
        {
            providers.Add(new GoogleDohProvider(_config.TimeoutSeconds, _fetcher));
        }

        if (selected.Contains("system-dns"))
        {
            providers.Add(new SystemDnsProvider());
        }

        if (selected.Contains("tor-exits"))
        {
            providers.Add(new TorExitsProvider(_config.TimeoutSeconds, _fetcher));
        }

        if (selected.Contains("cloud-ranges"))
        {
            providers.Add(new CloudRangesProvider(_config.TimeoutSeconds, _fetcher));
        }

        if (selected.Contains("abuseipdb") || _config.AbuseIpDbKey.Length != 0)
        {
            providers.Add(new AbuseIpDbProvider(_config.TimeoutSeconds, _fetcher, _config.AbuseIpDbKey));
        }

        return providers;
    }

    internal IntelligenceProfile Assemble(
        string target,
        GeoResult geo,
        IReadOnlyList<IntelEvidence> intel,
        IReadOnlyDictionary<string, ProviderHealth> health,
        long durationMs,
        bool sawCached)
    {
        // --- ASN layer: geo consensus first, RIPEstat enriches. ---
        AsnEvidence? ripe = intel.OfType<AsnEvidence>().FirstOrDefault(e => e.Success);
        string? asn = geo.Network.Asn ?? ripe?.Asns.FirstOrDefault();
        var asnInfo = new AsnIntelligence(
            asn,
            geo.Network.Organization ?? geo.Network.Isp ?? ripe?.Organization,
            ripe?.NetworkName ?? geo.Network.AsName,
            ripe?.Prefix,
            ripe?.Registry,
            geo.Geolocation.CountryCode ?? ripe?.Country,
            ripe?.Holder,
            SourcesFor(ripe, geo.Network.Asn is not null));

        // --- DNS layer: union of corroborating PTR answers. ---
        var ptrVotes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var dnsSources = new List<string>();
        foreach (DnsEvidence dns in intel.OfType<DnsEvidence>().Where(e => e.Success))
        {
            dnsSources.Add(dns.ProviderId);
            foreach (string host in dns.PtrHostnames)
            {
                if (!ptrVotes.TryGetValue(host, out List<string>? voters))
                {
                    voters = [];
                    ptrVotes[host] = voters;
                }

                voters.Add(dns.ProviderId);
            }
        }

        if (geo.Network.Hostname is not null
            && !ptrVotes.ContainsKey(geo.Network.Hostname))
        {
            ptrVotes[geo.Network.Hostname] = ["geo-consensus"];
        }

        string[] ptrs = [.. ptrVotes.Keys];
        string ptrConfidence = ptrs.Length == 0
            ? "UNKNOWN"
            : ptrVotes.Values.Any(v => v.Count >= 2) ? "HIGH"
            : ptrVotes.Count == 1 ? "MEDIUM" : "LOW";
        var dnsInfo = new DnsIntelligence(
            geo.Ip, ptrs, ptrConfidence, [.. dnsSources.Distinct(StringComparer.Ordinal)]);

        // --- Anonymity layer: evidence only, never inference. ---
        TorEvidence? tor = intel.OfType<TorEvidence>().FirstOrDefault(e => e.Success);
        CloudEvidence? cloud = intel.OfType<CloudEvidence>().FirstOrDefault(e => e.Success);
        ReputationEvidence? rep = intel.OfType<ReputationEvidence>().FirstOrDefault(e => e.Success);
        bool repOk = rep is not null;

        var torSignal = tor is null
            ? new AnonymitySignal(DetectionStatus.Unknown, "UNKNOWN", [], "Tor exit list unreachable.")
            : tor.IsExitNode
                ? new AnonymitySignal(DetectionStatus.Detected, "HIGH", ["tor-exits"],
                    $"IP is on the official Tor Project bulk exit list ({tor.ExitCount} exits tracked).")
                : new AnonymitySignal(DetectionStatus.NotDetected, "HIGH", ["tor-exits"],
                    "IP is absent from the official Tor exit list.");

        CloudMatch? cloudHit = cloud?.Matches.FirstOrDefault();
        var hostingSignal = cloud is null
            ? new AnonymitySignal(DetectionStatus.Unknown, "UNKNOWN", [], "Cloud-range feeds unreachable.")
            : cloudHit is not null
                ? new AnonymitySignal(DetectionStatus.Detected, "HIGH",
                    ["cloud-ranges"],
                    $"{cloudHit.Provider} {cloudHit.Service} {cloudHit.Prefix} (official feed).")
                : new AnonymitySignal(DetectionStatus.NotDetected, "LOW", ["cloud-ranges"],
                    "Not inside AWS/GCP/Cloudflare official ranges (other hosts possible).");

        bool vpnHit = repOk && rep!.UsageTypes.Any(u =>
            u.Contains("vpn", StringComparison.OrdinalIgnoreCase));
        bool proxyHit = repOk && rep!.UsageTypes.Any(u =>
            u.Contains("proxy", StringComparison.OrdinalIgnoreCase));
        var vpnSignal = !repOk
            ? new AnonymitySignal(DetectionStatus.Unknown, "UNKNOWN", [],
                "No proxy-intelligence source configured (optional AbuseIPDB key).")
            : vpnHit
                ? new AnonymitySignal(DetectionStatus.Detected, "MEDIUM", ["abuseipdb"],
                    "Reputation source classifies usage: " + string.Join(", ", rep!.UsageTypes))
                : new AnonymitySignal(DetectionStatus.NotDetected, "MEDIUM", ["abuseipdb"],
                    "Reputation source reports no VPN usage type.");
        var proxySignal = !repOk
            ? new AnonymitySignal(DetectionStatus.Unknown, "UNKNOWN", [],
                "No proxy-intelligence source configured (optional AbuseIPDB key).")
            : proxyHit
                ? new AnonymitySignal(DetectionStatus.Detected, "MEDIUM", ["abuseipdb"],
                    "Reputation source classifies usage: " + string.Join(", ", rep!.UsageTypes))
                : new AnonymitySignal(DetectionStatus.NotDetected, "MEDIUM", ["abuseipdb"],
                    "Reputation source reports no proxy usage type.");
        var anonymity = new AnonymityIntelligence(torSignal, vpnSignal, proxySignal, hostingSignal);

        // --- Risk layer. ---
        int? abuseScore = repOk ? rep!.AbuseConfidenceScore : null;
        RiskAssessment risk = RiskEngine.Evaluate(
            anonymity, abuseScore,
            RiskEngine.ParseWeights(_config.RiskWeights));

        // --- Provider outcomes (geo details + intel summaries). ---
        var outcomes = new List<ProviderOutcome>();
        outcomes.AddRange(geo.Providers.Select(d =>
            new ProviderOutcome(d.Name, d.Status, d.Error, d.Summary, null)));
        foreach (IntelEvidence evidence in intel)
        {
            outcomes.Add(new ProviderOutcome(
                evidence.ProviderId,
                evidence.Success ? "success" : "failed",
                evidence.Error,
                Summarize(evidence),
                null));
        }

        // --- Per-field confidence from the consensus votes. ---
        var fieldConfidences = new List<FieldConfidence>
        {
            Field("country", geo.Geolocation.Country, geo.ProvidersAgreeing, geo.ProvidersSuccessful),
            Field("region", geo.Geolocation.Region, geo.ProvidersAgreeing, geo.ProvidersSuccessful),
            Field("city", geo.Geolocation.City, geo.ProvidersAgreeing, geo.ProvidersSuccessful),
            Field("asn", geo.Network.Asn, geo.ProvidersAgreeing, geo.ProvidersSuccessful),
        };

        var metadata = new InvestigationMetadata(
            AppInfo.Version, DateTimeOffset.UtcNow, durationMs,
            [.. geo.Providers.Select(d => d.Name).Concat(intel.Select(e => e.ProviderId)).Distinct(StringComparer.Ordinal)],
            geo.ProvidersSuccessful + intel.Count(e => e.Success),
            sawCached);

        return new IntelligenceProfile(
            target, geo.IpVersion, false, geo, asnInfo, dnsInfo,
            anonymity, risk, fieldConfidences, outcomes, metadata);
    }

    private static FieldConfidence Field(string name, string? value, int agreeing, int total)
    {
        if (value is null)
        {
            return new FieldConfidence(name, null, "unknown", 0, total);
        }

        if (total <= 1)
        {
            return new FieldConfidence(name, value, "medium", agreeing, total);
        }

        double ratio = (double)agreeing / total;
        string confidence = ratio >= 1.0 ? "high" : ratio >= 0.5 ? "medium" : "low";
        return new FieldConfidence(name, value, confidence, agreeing, total);
    }

    private static string[] SourcesFor(AsnEvidence? ripe, bool geoHasAsn)
    {
        var sources = new List<string>();
        if (geoHasAsn)
        {
            sources.Add("geo-consensus");
        }

        if (ripe is not null)
        {
            sources.Add("ripestat");
        }

        return [.. sources];
    }

    private static string Summarize(IntelEvidence evidence)
        => evidence switch
        {
            TorEvidence t when t.Success => t.IsExitNode ? "Tor exit node" : "not a Tor exit",
            DnsEvidence d when d.Success => d.PtrHostnames.Length == 0
                ? "no PTR"
                : string.Join(", ", d.PtrHostnames.Take(2)),
            CloudEvidence c when c.Success => c.Matches.Length == 0
                ? "no cloud match"
                : $"{c.Matches[0].Provider} {c.Matches[0].Prefix}",
            AsnEvidence a when a.Success => string.Join(" ",
                new[] { a.Asns.FirstOrDefault(), a.Prefix }.Where(s => s is not null)),
            ReputationEvidence r when r.Success => r.AbuseConfidenceScore is int score
                ? $"abuse score {score}"
                : "checked",
            _ => evidence.Error ?? "failed",
        };
}
