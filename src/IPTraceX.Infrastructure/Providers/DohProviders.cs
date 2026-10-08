using System.Net;
using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>Shared DNS-over-HTTPS PTR logic (JSON API, no key).</summary>
public abstract class DohPtrProviderBase : IIntelProvider
{
    public abstract ProviderDescriptor Descriptor { get; }

    protected abstract string BuildUrl(string queryName);

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;

    protected DohPtrProviderBase(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
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
            string query = ReverseName(validated.Address);
            JsonElement? payload = await _fetcher.FetchAsync(
                BuildUrl(query), _timeoutSeconds,
                new Dictionary<string, string> { ["accept"] = "application/dns-json" },
                cancellationToken).ConfigureAwait(false);
            var names = new List<string>();
            if (payload is { } el && el.ValueKind == JsonValueKind.Object
                && el.TryGetProperty("Answer", out JsonElement answers)
                && answers.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement answer in answers.EnumerateArray())
                {
                    if (answer.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (answer.TryGetProperty("type", out JsonElement typeEl)
                        && typeEl.ValueKind == JsonValueKind.Number
                        && typeEl.GetInt32() == 12
                        && answer.TryGetProperty("data", out JsonElement dataEl)
                        && dataEl.ValueKind == JsonValueKind.String)
                    {
                        string name = dataEl.GetString()!.Trim().TrimEnd('.');
                        if (name.Length != 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            names.Add(name);
                        }
                    }
                }
            }

            return new DnsEvidence(Descriptor.Id, true, null, [.. names]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string message = ex is TraceXException tx ? tx.Message : ex.GetType().Name;
            return new DnsEvidence(Descriptor.Id, false, message, []);
        }
    }

    public static string ReverseName(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return $"{bytes[3]}.{bytes[2]}.{bytes[1]}.{bytes[0]}.in-addr.arpa";
        }

        // IPv6: nibble-reversed ip6.arpa (RFC 3597).
        var nibbles = new List<char>(64);
        for (int i = bytes.Length - 1; i >= 0; i--)
        {
            nibbles.Add(Nibble(bytes[i] & 0x0F));
            nibbles.Add('.');
            nibbles.Add(Nibble((bytes[i] >> 4) & 0x0F));
            nibbles.Add('.');
        }

        return new string([.. nibbles]) + "ip6.arpa";
    }

    private static char Nibble(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);
}

/// <summary>Reverse DNS via Cloudflare DoH (free, no key).</summary>
public sealed class CloudflareDohProvider : DohPtrProviderBase
{
    public override ProviderDescriptor Descriptor { get; } = new(
        "doh-cloudflare",
        "Cloudflare DoH",
        ProviderCategory.Dns,
        [4, 6],
        false,
        "No key required.",
        "Public resolver; fair use.");

    public CloudflareDohProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
        : base(timeoutSeconds, fetcher)
    {
    }

    protected override string BuildUrl(string queryName)
        => $"https://cloudflare-dns.com/dns-query?name={queryName}&type=PTR";
}

/// <summary>Reverse DNS via Google DoH (free, no key). Corroborates Cloudflare.</summary>
public sealed class GoogleDohProvider : DohPtrProviderBase
{
    public override ProviderDescriptor Descriptor { get; } = new(
        "doh-google",
        "Google DoH",
        ProviderCategory.Dns,
        [4, 6],
        false,
        "No key required.",
        "Public resolver; fair use.");

    public GoogleDohProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
        : base(timeoutSeconds, fetcher)
    {
    }

    protected override string BuildUrl(string queryName)
        => $"https://dns.google/resolve?name={queryName}&type=PTR";
}
