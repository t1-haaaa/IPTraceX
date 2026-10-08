using System.Text.Json;
using System.Text.Json.Nodes;
using IPTraceX.Core;
using IPTraceX.Infrastructure.Providers;

namespace IPTraceX.Infrastructure;

/// <summary>
/// Multi-provider GeoIP engine: validate once, ask each source
/// sequentially (reliability over raw speed), merge with consensus.
/// One provider failing never fails the lookup.
/// </summary>
public sealed class MultiProviderEngine
{
    public const string CacheSchema = "v2";

    private readonly AppConfig _config;
    private readonly List<IGeoProvider> _providers;

    public MultiProviderEngine(AppConfig config, IGeoJsonFetcher? fetcher = null)
    {
        _config = config;
        _providers = ProviderRegistry.Build(config, fetcher);
    }

    public MultiProviderEngine(AppConfig config, List<IGeoProvider> providers)
    {
        _config = config;
        _providers = providers;
    }

    public async Task<GeoResult> AnalyzeAsync(
        string ipText,
        Action<string>? onStage = null,
        CancellationToken cancellationToken = default)
    {
        onStage?.Invoke("validating");
        var validated = IpValidation.EnsurePublic(ipText);

        var successes = new List<GeoResult>();
        var details = new List<ProviderDetail>();
        foreach (IGeoProvider provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string ns = $"{CacheSchema}:{provider.Name}";
            JsonObject? cached = FileCache.Get(validated.Text, _config.CacheTtlSeconds, ns);
            if (cached is not null)
            {
                try
                {
                    GeoResult result = ResultFromJson(validated.Text, cached, provider.Name);
                    successes.Add(result);
                    details.Add(new ProviderDetail
                    {
                        Name = provider.Name,
                        Status = "success",
                        Freshness = "CACHED",
                        AgeSeconds = FileCache.GetAgeSeconds(validated.Text, _config.CacheTtlSeconds, ns),
                    });
                    onStage?.Invoke($"cached:{provider.Name}");
                    continue;
                }
                catch (Exception ex) when (ex is JsonException || ex is FormatException
                    || ex is InvalidOperationException || ex is ArgumentException)
                {
                    // Corrupt entry: fall through to a live lookup.
                }
            }

            onStage?.Invoke($"querying:{provider.Name}");
            try
            {
                GeoResult result = await provider.LookupAsync(validated.Text, cancellationToken)
                    .ConfigureAwait(false);
                result.Source = provider.Name;
                successes.Add(result);
                details.Add(new ProviderDetail
                {
                    Name = provider.Name,
                    Status = "success",
                    Freshness = "LIVE",
                });
                try
                {
                    FileCache.Put(validated.Text, GeoJson.FromResult(result), _config.CacheTtlSeconds, ns);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (_config.Debug)
                {
                    Console.Error.WriteLine(ex.ToString());
                }

                details.Add(new ProviderDetail
                {
                    Name = provider.Name,
                    Status = "failed",
                    Error = ShortError(ex),
                });
            }
        }

        if (successes.Count == 0)
        {
            string failures = string.Join("; ", details.Select(d => $"{d.Name}: {d.Error ?? "failed"}"));
            throw new ProviderException(
                $"All GeoIP providers failed ({details.Count}/{details.Count})."
                + (_config.Debug && failures.Length != 0 ? " " + failures : ""));
        }

        var successDetails = details.Where(d => d.Status == "success").ToList();
        for (int i = 0; i < successes.Count && i < successDetails.Count; i++)
        {
            var geo = successes[i].Geolocation;
            var parts = new List<string> { geo.Country ?? geo.CountryCode ?? "Unknown" };
            if (geo.Region is not null)
            {
                parts.Add(geo.Region);
            }
            else if (geo.City is not null)
            {
                parts.Add(geo.City);
            }

            successDetails[i].Summary = string.Join(" / ", parts);
        }

        Consensus.ConsensusOutcome consensus = Consensus.Build(successes);
        GeoResult final = consensus.Final;
        final.Providers.AddRange(details);
        final.ProvidersQueried = details.Count;
        final.ProvidersSuccessful = successes.Count;
        final.Votes.AddRange(successes);
        onStage?.Invoke("done");
        return final;
    }

    internal static GeoResult ResultFromJson(string ipText, JsonObject data, string source)
    {
        JsonObject geo = data["geolocation"]?.AsObject() ?? new JsonObject();
        JsonObject net = data["network"]?.AsObject() ?? new JsonObject();
        var result = new GeoResult
        {
            Ip = data["ip"]?.GetValue<string>() ?? ipText,
            IpVersion = data["ip_version"]?.GetValue<int>() ?? 4,
            GoogleMapsUrl = data["google_maps_url"]?.GetValue<string?>(),
            Source = source,
        };
        result.Geolocation.Country = geo["country"]?.GetValue<string?>();
        result.Geolocation.CountryCode = geo["country_code"]?.GetValue<string?>();
        result.Geolocation.Region = geo["region"]?.GetValue<string?>();
        result.Geolocation.RegionCode = geo["region_code"]?.GetValue<string?>();
        result.Geolocation.City = geo["city"]?.GetValue<string?>();
        result.Geolocation.PostalCode = geo["postal_code"]?.GetValue<string?>();
        result.Geolocation.Latitude = geo["latitude"]?.GetValue<double?>();
        result.Geolocation.Longitude = geo["longitude"]?.GetValue<double?>();
        result.Geolocation.Timezone = geo["timezone"]?.GetValue<string?>();
        result.Network.Isp = net["isp"]?.GetValue<string?>();
        result.Network.Organization = net["organization"]?.GetValue<string?>();
        result.Network.Asn = net["asn"]?.GetValue<string?>();
        result.Network.AsName = net["as_name"]?.GetValue<string?>();
        result.Network.Hostname = net["hostname"]?.GetValue<string?>();
        return result;
    }

    private static string ShortError(Exception ex)
    {
        string message = ex is TraceXException tx ? tx.Message : ex.GetType().Name + ": " + ex.Message;
        int newline = message.IndexOf('\n');
        if (newline >= 0)
        {
            message = message[..newline];
        }

        message = message.Trim();
        return message.Length > 160 ? message[..160] : message;
    }
}
