using System.Net;
using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>Minimal DoH JSON helper shared by email DNS queries.</summary>
internal static class Doh
{
    public sealed record DohAnswer(string Data);

    public static async Task<List<DohAnswer>> QueryAsync(
        string baseUrl,
        string name,
        string type,
        double timeoutSeconds,
        IGeoJsonFetcher fetcher,
        CancellationToken ct)
    {
        string url = $"{baseUrl}?name={Uri.EscapeDataString(name)}&type={type}";
        JsonElement? payload = await fetcher.FetchAsync(
            url, timeoutSeconds,
            new Dictionary<string, string> { ["accept"] = "application/dns-json" }, ct)
            .ConfigureAwait(false);
        var answers = new List<DohAnswer>();
        if (payload is not { } element || element.ValueKind != JsonValueKind.Object)
        {
            return answers;
        }

        if (!element.TryGetProperty("Answer", out JsonElement list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return answers;
        }

        foreach (JsonElement item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("data", out JsonElement data)
                && data.ValueKind == JsonValueKind.String
                && data.GetString() is string text
                && text.Length != 0)
            {
                answers.Add(new DohAnswer(text.Trim()));
            }
        }

        return answers;
    }

    public static async Task<bool> HasRecordAsync(
        string baseUrl,
        string name,
        double timeoutSeconds,
        IGeoJsonFetcher fetcher,
        CancellationToken ct,
        Func<string, bool> predicate)
    {
        foreach (DohAnswer answer in await QueryAsync(baseUrl, name, "TXT", timeoutSeconds, fetcher, ct)
                     .ConfigureAwait(false))
        {
            if (predicate(answer.Data.Trim('"')))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Email domain intelligence: MX/SPF/DMARC/DNSSEC via two DoH resolvers,
/// registrar metadata via RDAP, disposable/free-mail/mail-provider
/// classification from curated tables + live disposable service.
/// </summary>
public sealed class EmailDomainProvider : IEmailProvider
{
    public const string RdapBootstrap = "https://rdap.org/domain/";

    public ProviderDescriptor Descriptor { get; } = new(
        "email-domain",
        "Email Domain",
        ProviderCategory.Dns,
        [4, 6],
        false,
        "No key required.",
        "DoH resolvers + RDAP; fair use.");

    private static readonly string[] FreeMailDomains =
    [
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com",
        "msn.com", "yahoo.com", "ymail.com", "proton.me", "protonmail.com",
        "icloud.com", "me.com", "mac.com", "aol.com", "gmx.com", "gmx.net",
        "zoho.com", "yandex.com", "yandex.ru", "mail.ru", "bk.ru", "qq.com",
        "163.com", "126.com", "pm.me", "tutanota.com", "fastmail.com",
    ];

    private static readonly (string Fragment, string Provider)[] MailProviderHints =
    [
        ("google", "Google (Gmail / Workspace)"),
        ("outlook", "Microsoft 365 / Outlook"),
        ("hotmail", "Microsoft 365 / Outlook"),
        ("live", "Microsoft 365 / Outlook"),
        ("msn", "Microsoft 365 / Outlook"),
        ("yahoo", "Yahoo"),
        ("proton", "Proton Mail"),
        ("icloud", "Apple iCloud"),
        ("zoho", "Zoho"),
        ("yandex", "Yandex"),
        ("gmx", "GMX"),
        ("aol", "AOL"),
        ("mimecast", "Mimecast (filtering)"),
        ("pphosted", "Proofpoint (filtering)"),
        ("barracuda", "Barracuda (filtering)"),
        ("messagelabs", "Broadcom MessageLabs (filtering)"),
    ];

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;

    public EmailDomainProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
    {
        _timeoutSeconds = timeoutSeconds;
        _fetcher = fetcher ?? new HttpJsonClient();
    }

    public async Task<EmailEvidence> InvestigateAsync(
        EmailTarget target, EmailContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            string domain = target.Domain;
            var mx = new List<string>();
            foreach (string resolver in DohResolvers.All)
            {
                try
                {
                    foreach (Doh.DohAnswer answer in await Doh.QueryAsync(
                                 resolver, domain, "MX", _timeoutSeconds, _fetcher, cancellationToken)
                             .ConfigureAwait(false))
                    {
                        // MX format: "10 alt1.x." — keep the host only.
                        string host = answer.Data.Split(' ', 2) is [_, string rest]
                            ? rest.Trim().TrimEnd('.')
                            : answer.Data.Trim().TrimEnd('.');
                        if (host.Length != 0 && !mx.Contains(host, StringComparer.OrdinalIgnoreCase))
                        {
                            mx.Add(host);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One resolver failing never fails the provider.
                }
            }

            string? spf = await FirstTxtAsync(domain, s =>
                    s.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase), cancellationToken)
                .ConfigureAwait(false);
            string? dmarc = await FirstTxtAsync("_dmarc." + domain, s =>
                    s.StartsWith("v=DMARC1", StringComparison.OrdinalIgnoreCase), cancellationToken)
                .ConfigureAwait(false);

            string dnssec = await DnssecStatusAsync(domain, cancellationToken).ConfigureAwait(false);
            (string? registrar, DateTimeOffset? created, string? country) =
                await RdapAsync(domain, cancellationToken).ConfigureAwait(false);

            bool? disposable = await DisposableAsync(domain, cancellationToken).ConfigureAwait(false);

            return new EmailDomainEvidence(
                Descriptor.Id, true, null, [.. mx], spf, dmarc, dnssec,
                registrar, created, country,
                disposable, FreeMailDomains.Contains(domain, StringComparer.OrdinalIgnoreCase),
                ClassifyMailProvider(mx, domain));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string message = ex is TraceXException tx ? tx.Message : ex.GetType().Name;
            return new EmailDomainEvidence(
                Descriptor.Id, false, message, [], null, null, "UNKNOWN",
                null, null, null, null, false, null);
        }
    }

    private async Task<string?> FirstTxtAsync(
        string name, Func<string, bool> predicate, CancellationToken ct)
    {
        foreach (string resolver in DohResolvers.All)
        {
            try
            {
                foreach (Doh.DohAnswer answer in await Doh.QueryAsync(
                             resolver, name, "TXT", _timeoutSeconds, _fetcher, ct)
                         .ConfigureAwait(false))
                {
                    string text = answer.Data.Trim('"');
                    if (predicate(text))
                    {
                        return text.Length > 220 ? text[..220] : text;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }
        }

        return null;
    }

    private async Task<string> DnssecStatusAsync(string domain, CancellationToken ct)
    {
        // FOUND only when validating resolvers assert AD; else UNKNOWN
        // (absence of AD proves nothing about the zone).
        foreach (string resolver in DohResolvers.All)
        {
            try
            {
                JsonElement? payload = await _fetcher.FetchAsync(
                    $"{resolver}?name={Uri.EscapeDataString(domain)}&type=A",
                    _timeoutSeconds,
                    new Dictionary<string, string> { ["accept"] = "application/dns-json" }, ct)
                    .ConfigureAwait(false);
                if (payload is not { } element || element.ValueKind != JsonValueKind.Object)
                {
                    return "UNKNOWN";
                }

                if (!element.TryGetProperty("AD", out JsonElement ad)
                    || ad.ValueKind != JsonValueKind.True)
                {
                    return "UNKNOWN";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return "UNKNOWN";
            }
        }

        return "FOUND";
    }

    internal async Task<(string? Registrar, DateTimeOffset? Created, string? Country)> RdapAsync(
        string domain, CancellationToken ct)
    {
        try
        {
            JsonElement? payload = await _fetcher.FetchAsync(
                RdapBootstrap + Uri.EscapeDataString(domain),
                _timeoutSeconds, null, ct).ConfigureAwait(false);
            if (payload is not { } root || root.ValueKind != JsonValueKind.Object)
            {
                return (null, null, null);
            }

            string? registrar = null;
            string? country = null;
            DateTimeOffset? created = null;
            if (root.TryGetProperty("entities", out JsonElement entities)
                && entities.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entity in entities.EnumerateArray())
                {
                    if (entity.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    bool isRegistrar = entity.TryGetProperty("roles", out JsonElement roles)
                        && roles.ValueKind == JsonValueKind.Array
                        && roles.EnumerateArray().Any(r =>
                            r.ValueKind == JsonValueKind.String
                            && r.GetString()!.Equals("registrar", StringComparison.OrdinalIgnoreCase));
                    if (!isRegistrar)
                    {
                        continue;
                    }

                    registrar ??= VcardFn(entity);
                    foreach (JsonElement sub in entity.TryGetProperty("entities", out JsonElement subs)
                                 && subs.ValueKind == JsonValueKind.Array
                                 ? subs.EnumerateArray()
                                 : Enumerable.Empty<JsonElement>())
                    {
                        if (sub.ValueKind == JsonValueKind.Object)
                        {
                            country ??= VcardCountry(sub);
                        }
                    }
                }
            }

            if (root.TryGetProperty("events", out JsonElement events)
                && events.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in events.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("eventAction", out JsonElement action)
                        && action.ValueKind == JsonValueKind.String
                        && action.GetString()!.Equals("registration", StringComparison.OrdinalIgnoreCase)
                        && item.TryGetProperty("eventDate", out JsonElement date)
                        && date.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(date.GetString(), out DateTimeOffset parsed))
                    {
                        created = parsed;
                        break;
                    }
                }
            }

            return (registrar, created, country);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, null, null);
        }
    }

    private static string? VcardFn(JsonElement entity)
    {
        if (!entity.TryGetProperty("vcardArray", out JsonElement vcard)
            || vcard.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement part in vcard.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var items = part.EnumerateArray().ToList();
            // Case A: part itself is a property: ["fn", {}, "text", "value"].
            if (IsFnProperty(items))
            {
                return items[3].GetString()?.Trim();
            }

            // Case B: RDAP nesting: ["vcard", [["version", ...], ["fn", ...]]].
            // Part is the property list; each entry is itself a property array.
            foreach (JsonElement inner in items)
            {
                if (inner.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var props = inner.EnumerateArray().ToList();
                if (IsFnProperty(props))
                {
                    return props[3].GetString()?.Trim();
                }
            }
        }

        return null;
    }

    private static bool IsFnProperty(List<JsonElement> items)
        => items.Count >= 4
            && items[0].ValueKind == JsonValueKind.String
            && items[0].GetString() == "fn"
            && items[3].ValueKind == JsonValueKind.String;

    private static string? VcardCountry(JsonElement entity)
    {
        if (!entity.TryGetProperty("vcardArray", out JsonElement vcard)
            || vcard.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement part in vcard.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var items = part.EnumerateArray().ToList();
            string? country = CountryFromAdr(items);
            if (country is not null)
            {
                return country;
            }

            foreach (JsonElement inner in items)
            {
                if (inner.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                country = CountryFromAdr(inner.EnumerateArray().ToList());
                if (country is not null)
                {
                    return country;
                }
            }
        }

        return null;
    }

    private static string? CountryFromAdr(List<JsonElement> items)
    {
        if (items.Count >= 4
            && items[0].ValueKind == JsonValueKind.String
            && items[0].GetString() == "adr"
            && items[3].ValueKind == JsonValueKind.Array)
        {
            var fields = items[3].EnumerateArray().ToList();
            if (fields.Count >= 7
                && fields[6].ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(fields[6].GetString()))
            {
                return fields[6].GetString()!.Trim();
            }
        }

        return null;
    }

    public async Task<bool?> DisposableAsync(string domain, CancellationToken ct)
    {
        try
        {
            JsonElement? payload = await _fetcher.FetchAsync(
                $"https://open.kickbox.com/v1/disposable/{Uri.EscapeDataString(domain)}",
                _timeoutSeconds, null, ct).ConfigureAwait(false);
            if (payload is { } element
                && element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("disposable", out JsonElement flag))
            {
                return flag.ValueKind == JsonValueKind.True;
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Service unreachable: unknown, never a guess.
            return null;
        }
    }

    public static string? ClassifyMailProvider(IReadOnlyList<string> mxHosts, string domain)
    {
        foreach (string host in mxHosts)
        {
            string lower = host.ToLowerInvariant();
            foreach (var (fragment, provider) in MailProviderHints)
            {
                if (lower.Contains(fragment, StringComparison.Ordinal))
                {
                    return provider;
                }
            }
        }

        if (mxHosts.Count == 1
            && mxHosts[0].Equals(domain, StringComparison.OrdinalIgnoreCase))
        {
            return "Self-hosted (MX points at the domain itself)";
        }

        return null;
    }
}

/// <summary>DoH resolver endpoints (both queried for corroboration).</summary>
internal static class DohResolvers
{
    public static readonly string[] All =
    [
        "https://cloudflare-dns.com/dns-query",
        "https://dns.google/resolve",
    ];
}
