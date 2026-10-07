using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure;

/// <summary>Detects the machine's public exit IP over HTTPS (ipify, no key).</summary>
public static class SelfIp
{
    public static async Task<string> DetectAsync(
        double timeoutSeconds,
        IGeoJsonFetcher? fetcher = null,
        CancellationToken cancellationToken = default)
    {
        fetcher ??= new HttpJsonClient();
        JsonElement? payload = await fetcher.FetchAsync(
            "https://api.ipify.org?format=json", timeoutSeconds, null, cancellationToken)
            .ConfigureAwait(false);
        if (payload is null
            || payload.Value.ValueKind != JsonValueKind.Object
            || !payload.Value.TryGetProperty("ip", out JsonElement ipEl)
            || ipEl.ValueKind != JsonValueKind.String)
        {
            throw new BadResponseException("Self-IP service returned an unexpected response.");
        }

        return IpValidation.EnsurePublic(ipEl.GetString()!).Text;
    }
}
