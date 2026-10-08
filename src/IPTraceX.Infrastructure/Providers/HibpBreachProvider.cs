using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>
/// Breach metadata via Have I Been Pwned v3 (OPTIONAL key via
/// IPTraceX_HIBP_API_KEY). Skipped cleanly without a key. Reports safe
/// metadata only (name, domain, date, data classes) — never passwords,
/// hashes, tokens or dumps, which the API does not return anyway.
/// </summary>
public sealed class HibpBreachProvider : IEmailProvider
{
    public ProviderDescriptor Descriptor { get; } = new(
        "hibp",
        "Have I Been Pwned",
        ProviderCategory.Breach,
        [4, 6],
        true,
        "Requires IPTraceX_HIBP_API_KEY (paid HIBP subscription).",
        "HIBP rate limits; User-Agent required by ToS.");

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;
    private readonly string _apiKey;

    public HibpBreachProvider(
        double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null, string apiKey = "")
    {
        _timeoutSeconds = timeoutSeconds;
        _fetcher = fetcher ?? new HttpJsonClient();
        _apiKey = apiKey.Trim();
    }

    public bool IsConfigured => _apiKey.Length != 0;

    public async Task<EmailEvidence> InvestigateAsync(
        EmailTarget target, EmailContext context, CancellationToken cancellationToken = default)
    {
        string key = _apiKey.Length != 0 ? _apiKey : context.HibpApiKey ?? "";
        if (key.Length == 0)
        {
            return new BreachEvidence(Descriptor.Id, false, "not configured (no API key)", []);
        }

        try
        {
            JsonElement? payload;
            try
            {
                payload = await _fetcher.FetchAsync(
                    "https://haveibeenpwned.com/api/v3/breachedaccount/"
                    + Uri.EscapeDataString(target.Normalized)
                    + "?truncateResponse=false",
                    _timeoutSeconds,
                    new Dictionary<string, string> { ["hibp-api-key"] = key },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (NotFoundException)
            {
                // 404 = address not found in the HIBP corpus.
                return new BreachEvidence(Descriptor.Id, true, null, []);
            }

            var breaches = new List<BreachInfo>();
            if (payload is { } root && root.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string? name = item.TryGetProperty("Name", out JsonElement nameEl)
                        && nameEl.ValueKind == JsonValueKind.String
                        ? nameEl.GetString()?.Trim() : null;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var categories = new List<string>();
                    if (item.TryGetProperty("DataClasses", out JsonElement classes)
                        && classes.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement category in classes.EnumerateArray())
                        {
                            if (category.ValueKind == JsonValueKind.String
                                && category.GetString() is string text
                                && text.Length != 0)
                            {
                                categories.Add(text);
                            }
                        }
                    }

                    breaches.Add(new BreachInfo(
                        name,
                        item.TryGetProperty("Domain", out JsonElement domainEl)
                            && domainEl.ValueKind == JsonValueKind.String
                            ? domainEl.GetString()?.Trim() : null,
                        item.TryGetProperty("BreachDate", out JsonElement dateEl)
                            && dateEl.ValueKind == JsonValueKind.String
                            ? dateEl.GetString()?.Trim() : null,
                        [.. categories],
                        "HIBP"));
                }
            }

            return new BreachEvidence(Descriptor.Id, true, null, [.. breaches]);
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
}
