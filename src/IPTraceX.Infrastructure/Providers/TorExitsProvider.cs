using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>
/// Tor exit-node detection from the official Tor Project bulk exit list
/// (free, no key). Cached 6 hours; a list hit is HIGH-confidence evidence.
/// </summary>
public sealed class TorExitsProvider : IIntelProvider
{
    public const string ListUrl = "https://check.torproject.org/torbulkexitlist";
    public const int ListTtlSeconds = 6 * 3600;

    public ProviderDescriptor Descriptor { get; } = new(
        "tor-exits",
        "Tor Exits",
        ProviderCategory.Security,
        [4, 6],
        false,
        "No key required.",
        "One small list download, cached 6 hours.");

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;

    public TorExitsProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
    {
        _timeoutSeconds = timeoutSeconds;
        _fetcher = fetcher ?? new HttpJsonClient();
    }

    public async Task<IntelEvidence> InvestigateAsync(
        string ip, ProviderContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var validated = IpValidation.EnsurePublic(ip);
            HashSet<string> exits = await LoadExitSetAsync(cancellationToken).ConfigureAwait(false);
            return new TorEvidence(
                Descriptor.Id, true, null,
                exits.Contains(validated.Text), exits.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string message = ex is TraceXException tx ? tx.Message : ex.GetType().Name;
            return new TorEvidence(Descriptor.Id, false, message, false, 0);
        }
    }

    internal async Task<HashSet<string>> LoadExitSetAsync(CancellationToken cancellationToken = default)
    {
        string? cached = FileCache.GetText("tor-bulk-exit-list", ListTtlSeconds, "v1");
        if (cached is not null)
        {
            return Parse(cached);
        }

        string text = await _fetcher.FetchTextAsync(ListUrl, _timeoutSeconds, cancellationToken)
            .ConfigureAwait(false);
        FileCache.PutText("tor-bulk-exit-list", text, ListTtlSeconds, "v1");
        return Parse(text);
    }

    internal static HashSet<string> Parse(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            try
            {
                var parsed = IpValidation.EnsurePublic(line);
                set.Add(parsed.Text);
            }
            catch (InvalidIpException)
            {
                // Ignore malformed list lines; the dataset stays usable.
            }
        }

        return set;
    }
}
