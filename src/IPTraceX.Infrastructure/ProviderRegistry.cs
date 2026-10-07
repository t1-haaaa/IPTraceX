using IPTraceX.Core;
using IPTraceX.Infrastructure.Providers;

namespace IPTraceX.Infrastructure;

/// <summary>Provider registry: names, aliases, default priority order.</summary>
public static class ProviderRegistry
{
    public static readonly string[] DefaultOrder = ["ipwho.is", "ipapi.co", "ipinfo.io"];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ipwho.is"] = "ipwho.is",
        ["ipwhois"] = "ipwho.is",
        ["primary"] = "ipwho.is",
        ["ipapi.co"] = "ipapi.co",
        ["ipapi"] = "ipapi.co",
        ["ipinfo.io"] = "ipinfo.io",
        ["ipinfo"] = "ipinfo.io",
    };

    public static string[] ResolveNames(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [.. DefaultOrder];
        }

        var names = new List<string>();
        foreach (string part in raw.Split(','))
        {
            string key = part.Trim();
            if (key.Length == 0)
            {
                continue;
            }

            if (!Aliases.TryGetValue(key, out string? canonical))
            {
                throw new UsageException(
                    $"Unknown provider: '{key}'. Available: ipwho.is, ipapi.co, ipinfo.io.");
            }

            if (!names.Contains(canonical, StringComparer.Ordinal))
            {
                names.Add(canonical);
            }
        }

        return names.Count == 0 ? [.. DefaultOrder] : [.. names];
    }

    public static List<IGeoProvider> Build(AppConfig config, IGeoJsonFetcher? fetcher = null)
    {
        var providers = new List<IGeoProvider>();
        foreach (string name in config.Providers)
        {
            if (name == "ipwho.is")
            {
                providers.Add(new IpWhoIsProvider(config.TimeoutSeconds, fetcher));
            }
            else if (name == "ipapi.co")
            {
                providers.Add(new IpApiCoProvider(config.TimeoutSeconds, fetcher));
            }
            else if (name == "ipinfo.io")
            {
                providers.Add(new IpInfoProvider(config.TimeoutSeconds, fetcher, config.IpInfoToken));
            }
        }

        return providers;
    }
}
