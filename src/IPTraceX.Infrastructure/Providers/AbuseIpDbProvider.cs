using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>
/// AbuseIPDB reputation (OPTIONAL key via IPTraceX_ABUSEIPDB_KEY).
/// Skipped cleanly when no key is configured. When present, contributes
/// abuse-confidence scoring plus VPN/proxy/Tor usage-type signals.
/// Docs: https://docs.abuseipdb.com/
/// </summary>
public sealed class AbuseIpDbProvider : IIntelProvider
{
    public ProviderDescriptor Descriptor { get; } = new(
        "abuseipdb",
        "AbuseIPDB",
        ProviderCategory.Reputation,
        [4],
        true,
        "Requires IPTraceX_ABUSEIPDB_KEY (free tier available).",
        "Free tier: 1000 lookups/day.");

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;
    private readonly string _apiKey;

    public AbuseIpDbProvider(
        double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null, string apiKey = "")
    {
        _timeoutSeconds = timeoutSeconds;
        _fetcher = fetcher ?? new HttpJsonClient();
        _apiKey = apiKey.Trim();
    }

    public bool IsConfigured => _apiKey.Length != 0;

    public async Task<IntelEvidence> InvestigateAsync(
        string ip, ProviderContext context, CancellationToken cancellationToken = default)
    {
        string key = _apiKey.Length != 0 ? _apiKey : context.AbuseIpDbKey ?? "";
        if (key.Length == 0)
        {
            return new ReputationEvidence(Descriptor.Id, false, "not configured (no API key)", null, [], null, null);
        }

        try
        {
            var validated = IpValidation.EnsurePublic(ip);
            JsonElement? payload = await _fetcher.FetchAsync(
                $"https://api.abuseipdb.com/api/v2/check?ipAddress={validated.Text}&maxAgeInDays=90&verbose=true",
                _timeoutSeconds,
                new Dictionary<string, string>
                {
                    ["Key"] = key,
                    ["Accept"] = "application/json",
                },
                cancellationToken).ConfigureAwait(false);
            if (payload is null || payload.Value.ValueKind != JsonValueKind.Object)
            {
                throw new BadResponseException("Provider returned an unexpected response shape.");
            }

            if (!payload.Value.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Object)
            {
                throw new BadResponseException("Provider returned an unexpected response shape.");
            }

            int? score = data.TryGetProperty("abuseConfidenceScore", out JsonElement scoreEl)
                && scoreEl.ValueKind == JsonValueKind.Number
                && scoreEl.TryGetInt32(out int parsedScore)
                ? parsedScore
                : null;
            var usage = new List<string>();
            if (data.TryGetProperty("usageType", out JsonElement usageEl)
                && usageEl.ValueKind == JsonValueKind.String)
            {
                string? raw = usageEl.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    usage.Add(raw.Trim());
                }
            }

            return new ReputationEvidence(
                Descriptor.Id, true, null, score, [.. usage],
                JsonFields.Str(data, "isp"), JsonFields.Str(data, "countryCode"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string message = ex is TraceXException tx ? tx.Message : ex.GetType().Name;
            return new ReputationEvidence(Descriptor.Id, false, message, null, [], null, null);
        }
    }
}
