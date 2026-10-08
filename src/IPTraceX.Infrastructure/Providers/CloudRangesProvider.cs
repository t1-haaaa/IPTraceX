using System.Net;
using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>
/// Hosting/cloud classification from OFFICIAL provider range feeds only:
/// AWS ip-ranges.json, GCP cloud.json, Cloudflare ips-v4/ips-v6.
/// A match is HIGH-confidence evidence (exact prefix + source file).
/// Feeds cached 24h; longest-prefix match wins.
/// </summary>
public sealed class CloudRangesProvider : IIntelProvider
{
    public const int FeedsTtlSeconds = 24 * 3600;

    public ProviderDescriptor Descriptor { get; } = new(
        "cloud-ranges",
        "Cloud Ranges",
        ProviderCategory.Cloud,
        [4, 6],
        false,
        "No key required.",
        "Official feeds, cached 24 hours.");

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;

    public CloudRangesProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
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
            var matches = new List<CloudMatch>();
            foreach (CloudMatch match in await MatchAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (PrefixContains(match.Prefix, validated.Address))
                {
                    matches.Add(match);
                }
            }

            // Longest (most specific) prefix first.
            matches.Sort((a, b) => PrefixLength(b.Prefix).CompareTo(PrefixLength(a.Prefix)));
            return new CloudEvidence(Descriptor.Id, true, null, [.. matches]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string message = ex is TraceXException tx ? tx.Message : ex.GetType().Name;
            return new CloudEvidence(Descriptor.Id, false, message, []);
        }
    }

    internal async Task<List<CloudMatch>> MatchAllAsync(CancellationToken cancellationToken = default)
    {
        var matches = new List<CloudMatch>();
        matches.AddRange(await AmazonAsync(cancellationToken).ConfigureAwait(false));
        matches.AddRange(await GoogleAsync(cancellationToken).ConfigureAwait(false));
        matches.AddRange(await CloudflareAsync(cancellationToken).ConfigureAwait(false));
        return matches;
    }

    private async Task<List<CloudMatch>> AmazonAsync(CancellationToken ct)
    {
        const string key = "cloud-feed-amazon";
        string? text = FileCache.GetText(key, FeedsTtlSeconds, "v1");
        text ??= await _fetcher.FetchTextAsync(
            "https://ip-ranges.amazonaws.com/ip-ranges.json", _timeoutSeconds, ct)
            .ConfigureAwait(false);
        FileCache.PutText(key, text, FeedsTtlSeconds, "v1");
        var matches = new List<CloudMatch>();
        using JsonDocument doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return matches;
        }

        foreach (string field in new[] { "prefixes", "ipv6_prefixes" })
        {
            if (!doc.RootElement.TryGetProperty(field, out JsonElement arr)
                || arr.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? prefix = field == "prefixes"
                    ? JsonFields.Str(item, "ip_prefix")
                    : JsonFields.Str(item, "ipv6_prefix");
                string service = JsonFields.Str(item, "service") ?? "AMAZON";
                if (prefix is not null)
                {
                    matches.Add(new CloudMatch("AWS", service, prefix));
                }
            }
        }

        return matches;
    }

    private async Task<List<CloudMatch>> GoogleAsync(CancellationToken ct)
    {
        const string key = "cloud-feed-google";
        string? text = FileCache.GetText(key, FeedsTtlSeconds, "v1");
        text ??= await _fetcher.FetchTextAsync(
            "https://www.gstatic.com/ipranges/cloud.json", _timeoutSeconds, ct)
            .ConfigureAwait(false);
        FileCache.PutText(key, text, FeedsTtlSeconds, "v1");
        var matches = new List<CloudMatch>();
        using JsonDocument doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("prefixes", out JsonElement arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? prefix = JsonFields.Str(item, "ipv4Prefix") ?? JsonFields.Str(item, "ipv6Prefix");
                string service = JsonFields.Str(item, "service") ?? "Google Cloud";
                if (prefix is not null)
                {
                    matches.Add(new CloudMatch("GCP", service, prefix));
                }
            }
        }

        return matches;
    }

    private async Task<List<CloudMatch>> CloudflareAsync(CancellationToken ct)
    {
        var matches = new List<CloudMatch>();
        foreach ((string name, string url) in new[]
                 {
                     ("ips-v4", "https://www.cloudflare.com/ips-v4"),
                     ("ips-v6", "https://www.cloudflare.com/ips-v6"),
                 })
        {
            string key = "cloud-feed-" + name;
            string? text = FileCache.GetText(key, FeedsTtlSeconds, "v1");
            text ??= await _fetcher.FetchTextAsync(url, _timeoutSeconds, ct).ConfigureAwait(false);
            FileCache.PutText(key, text, FeedsTtlSeconds, "v1");
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length != 0)
                {
                    matches.Add(new CloudMatch("Cloudflare", "Cloudflare", line));
                }
            }
        }

        return matches;
    }

    public static bool PrefixContains(string prefix, IPAddress address)
    {
        try
        {
            int slash = prefix.IndexOf('/');
            if (slash < 0)
            {
                return false;
            }

            if (!IPAddress.TryParse(prefix[..slash].Trim(), out IPAddress? network) || network is null)
            {
                return false;
            }

            if (!int.TryParse(prefix[(slash + 1)..].Trim(), out int length))
            {
                return false;
            }

            byte[] addr = address.GetAddressBytes();
            byte[] net = network.GetAddressBytes();
            if (addr.Length != net.Length)
            {
                return false;
            }

            int maxBits = addr.Length * 8;
            if (length < 0 || length > maxBits)
            {
                return false;
            }

            int fullBytes = length / 8;
            int restBits = length % 8;
            for (int i = 0; i < fullBytes; i++)
            {
                if (addr[i] != net[i])
                {
                    return false;
                }
            }

            if (restBits > 0)
            {
                byte mask = (byte)(0xFF << (8 - restBits));
                if ((addr[fullBytes] & mask) != (net[fullBytes] & mask))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is FormatException)
        {
            return false;
        }
    }

    public static int PrefixLength(string prefix)
    {
        int slash = prefix.IndexOf('/');
        if (slash < 0 || !int.TryParse(prefix[(slash + 1)..].Trim(), out int length))
        {
            return -1;
        }

        return length;
    }
}
