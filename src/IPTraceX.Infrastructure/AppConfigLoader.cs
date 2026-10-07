using IPTraceX.Core;
using Microsoft.Extensions.Configuration;

namespace IPTraceX.Infrastructure;

/// <summary>
/// Configuration loader backed by Microsoft.Extensions.Configuration.
/// Sources (later wins): .env file values, then real environment.
/// New IPTraceX_* names with legacy IPGHOST_* fallback; never any secrets
/// in source. Values are clamped/validated exactly like the former tool.
/// </summary>
public static class AppConfigLoader
{
    public static AppConfig Load(string projectRoot)
    {
        var dotenv = ReadDotEnv(Path.Combine(projectRoot, ".env"));

        IConfigurationRoot config = new ConfigurationBuilder()
            .AddInMemoryCollection(dotenv)
            .AddEnvironmentVariables()
            .Build();

        string Get(params string[] names)
        {
            foreach (string name in names)
            {
                string? value = config[name];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return "";
        }

        double timeout = 10.0;
        if (double.TryParse(
                Get("IPTRACEX_TIMEOUT", "IPGHOST_TIMEOUT"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsedTimeout))
        {
            timeout = parsedTimeout;
        }

        timeout = Math.Min(Math.Max(timeout, 1.0), 60.0);

        int ttl = 3600;
        if (int.TryParse(Get("IPTRACEX_CACHE_TTL", "IPGHOST_CACHE_TTL"), out int parsedTtl))
        {
            ttl = parsedTtl;
        }

        ttl = Math.Max(ttl, 0);

        bool noColor = IsTruthy(Get("IPTRACEX_NO_COLOR", "IPGHOST_NO_COLOR"))
            || Get("NO_COLOR").Length != 0;
        bool debug = IsTruthy(Get("IPTRACEX_DEBUG", "IPGHOST_DEBUG"));

        return new AppConfig
        {
            ApiKey = Get("IPTRACEX_API_KEY", "IPGHOST_API_KEY"),
            TimeoutSeconds = timeout,
            CacheTtlSeconds = ttl,
            NoColor = noColor,
            Debug = debug,
            ProjectRoot = projectRoot,
            Providers = ProviderRegistry.ResolveNames(Get("IPTRACEX_PROVIDERS", "IPGHOST_PROVIDERS")),
            IpInfoToken = Get("IPINFO_TOKEN"),
        };
    }

    private static bool IsTruthy(string value)
        => value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    internal static IEnumerable<KeyValuePair<string, string?>> ReadDotEnv(string path)
    {
        var pairs = new List<KeyValuePair<string, string?>>();
        string[] lines;
        try
        {
            if (!File.Exists(path))
            {
                return pairs;
            }

            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return pairs;
        }

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || !line.Contains('='))
            {
                continue;
            }

            int index = line.IndexOf('=');
            string key = line[..index].Trim();
            string value = line[(index + 1)..].Trim().Trim('"', '\'', ' ');
            if (key.Length != 0)
            {
                pairs.Add(new KeyValuePair<string, string?>(key, value));
            }
        }

        return pairs;
    }
}
