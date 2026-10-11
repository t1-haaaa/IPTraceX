using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>
/// Breach-name intelligence via XposedOrNot (FREE, no key, fair use).
/// Reports breach NAMES only — no data classes, no dates, no dumps.
/// Password exposure therefore stays NOT_REPORTED on XON-only data;
/// pair with HIBP (key-gated) for data-class depth.
/// </summary>
public sealed class XposedOrNotProvider : IEmailProvider
{
    public const string ApiBase = "https://api.xposedornot.com/v1/check-email/";

    public ProviderDescriptor Descriptor { get; } = new(
        "xon",
        "XposedOrNot",
        ProviderCategory.Breach,
        [4, 6],
        false,
        "No key required (free public API).",
        "Fair-use rate limits; User-Agent sent.");

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;

    public XposedOrNotProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
    {
        _timeoutSeconds = timeoutSeconds;
        _fetcher = fetcher ?? new HttpJsonClient();
    }

    public async Task<EmailEvidence> InvestigateAsync(
        EmailTarget target, EmailContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            JsonElement? payload = await _fetcher.FetchAsync(
                ApiBase + Uri.EscapeDataString(target.Normalized),
                _timeoutSeconds, null, cancellationToken).ConfigureAwait(false);
            return new BreachEvidence(Descriptor.Id, true, null, ParseNames(payload));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string message = ex is TraceXException tx ? tx.Message : ex.GetType().Name;
            return new BreachEvidence(Descriptor.Id, false, message, []);
        }
    }

    internal static BreachInfo[] ParseNames(JsonElement? payload)
    {
        if (payload is not { } root || root.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        // Absent address: {"Error": "Not found", "email": null} (HTTP 200).
        if (!root.TryGetProperty("breaches", out JsonElement breaches)
            || breaches.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<BreachInfo>();
        foreach (JsonElement group in breaches.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement item in group.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String
                    && item.GetString() is string name
                    && !string.IsNullOrWhiteSpace(name))
                {
                    string clean = name.Trim();
                    if (clean.Length > 120)
                    {
                        clean = clean[..120];
                    }

                    names.Add(new BreachInfo(clean, null, null, [], "XON"));
                }
            }
        }

        return [.. names];
    }
}
