using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>
/// RIPEstat ASN/registry intelligence (free, HTTPS, no key).
/// Combines network-info (covering prefix + ASNs) with whois records
/// (NetName, Organization, registry country). IPv4 + IPv6.
/// </summary>
public sealed class RipeStatProvider : IIntelProvider
{
    public ProviderDescriptor Descriptor { get; } = new(
        "ripestat",
        "RIPEstat",
        ProviderCategory.Asn,
        [4, 6],
        false,
        "No key required.",
        "Fair use; tiny responses, sequential calls.");

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;

    public RipeStatProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
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
            string target = validated.Text;
            JsonElement? net = await _fetcher.FetchAsync(
                $"https://stat.ripe.net/data/network-info/data.json?resource={target}",
                _timeoutSeconds, null, cancellationToken).ConfigureAwait(false);
            JsonElement? whois = await _fetcher.FetchAsync(
                $"https://stat.ripe.net/data/whois/data.json?resource={target}",
                _timeoutSeconds, null, cancellationToken).ConfigureAwait(false);

            string? prefix = null;
            var asns = new List<string>();
            if (net is { } netEl && netEl.ValueKind == JsonValueKind.Object
                && netEl.TryGetProperty("data", out JsonElement netData)
                && netData.ValueKind == JsonValueKind.Object)
            {
                prefix = JsonFields.Str(netData, "prefix");
                if (netData.TryGetProperty("asns", out JsonElement asnArr)
                    && asnArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in asnArr.EnumerateArray())
                    {
                        string? raw = item.ValueKind == JsonValueKind.String
                            ? item.GetString()?.Trim()
                            : item.ValueKind == JsonValueKind.Number
                                ? item.GetRawText()
                                : null;
                        if (!string.IsNullOrEmpty(raw))
                        {
                            string digits = new string(raw.Where(char.IsDigit).ToArray());
                            if (digits.Length != 0)
                            {
                                asns.Add("AS" + digits);
                            }
                        }
                    }
                }
            }

            string? networkName = null;
            string? organization = null;
            string? country = null;
            if (whois is { } whoisEl && whoisEl.ValueKind == JsonValueKind.Object
                && whoisEl.TryGetProperty("data", out JsonElement whoisData)
                && whoisData.ValueKind == JsonValueKind.Object
                && whoisData.TryGetProperty("records", out JsonElement records)
                && records.ValueKind == JsonValueKind.Array)
            {
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonElement recordSet in records.EnumerateArray())
                {
                    if (recordSet.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (JsonElement rec in recordSet.EnumerateArray())
                    {
                        if (rec.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        string? key = JsonFields.Str(rec, "key");
                        string? value = JsonFields.Str(rec, "value");
                        if (key is not null && value is not null && !fields.ContainsKey(key))
                        {
                            fields[key] = value;
                        }
                    }
                }

                fields.TryGetValue("NetName", out networkName);
                fields.TryGetValue("Organization", out organization);
                fields.TryGetValue("Country", out country);
            }

            return new AsnEvidence(
                Descriptor.Id, true, null, prefix, [.. asns],
                Holder: null, Registry: null, organization, networkName, country);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AsnEvidence(Descriptor.Id, false, Short(ex), null, [], null, null, null, null, null);
        }
    }

    private static string Short(Exception ex)
    {
        string message = ex is TraceXException tx ? tx.Message : ex.GetType().Name;
        int newline = message.IndexOf('\n');
        if (newline >= 0)
        {
            message = message[..newline];
        }

        message = message.Trim();
        return message.Length > 160 ? message[..160] : message;
    }
}
