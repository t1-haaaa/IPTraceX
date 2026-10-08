using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IPTraceX.Core;

/// <summary>
/// Tiny file cache. Stores normalized result dicts only -- never secrets.
/// Provider-aware: entries are namespaced per provider plus schema version.
/// </summary>
public static class FileCache
{
    public static string CacheDir()
    {
        string? baseDir = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        string root = string.IsNullOrEmpty(baseDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "iptracex")
            : Path.Combine(baseDir, "iptracex");
        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }

        return root;
    }

    internal static string PathFor(string ip, string ns = "")
    {
        string material = ns.Length == 0 ? ip.Trim().ToLowerInvariant() : ns + "\0" + ip.Trim().ToLowerInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        string digest = Convert.ToHexString(hash).ToLowerInvariant()[..32];
        return Path.Combine(CacheDir(), digest + ".json");
    }

    public static JsonObject? Get(string ip, int ttlSeconds, string ns = "")
    {
        if (ttlSeconds <= 0)
        {
            return null;
        }

        string path = PathFor(ip, ns);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("_saved_at", out JsonElement savedEl))
            {
                return null;
            }

            double saved = savedEl.GetDouble();
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (now - saved > ttlSeconds)
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty("data", out JsonElement dataEl)
                || dataEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return JsonNode.Parse(dataEl.GetRawText())?.AsObject();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
            || ex is JsonException || ex is FormatException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Age of a cached entry in seconds, or null when absent/expired.</summary>
    public static double? GetAgeSeconds(string key, int ttlSeconds, string ns = "")
    {
        if (ttlSeconds <= 0)
        {
            return null;
        }

        string path = PathFor(key, ns);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("_saved_at", out JsonElement savedEl))
            {
                return null;
            }

            double saved = savedEl.GetDouble();
            double age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - saved;
            if (age > ttlSeconds || age < 0)
            {
                return null;
            }

            return age;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
            || ex is JsonException || ex is FormatException)
        {
            return null;
        }
    }

    public static void Put(string ip, JsonObject data, int ttlSeconds, string ns = "")
    {
        if (ttlSeconds <= 0)
        {
            return;
        }

        // Refuse to cache anything that looks like it contains a secret.
        string lowered = data.ToJsonString().ToLowerInvariant();
        if (lowered.Contains("api_key") || lowered.Contains("apikey") || lowered.Contains("bearer"))
        {
            return;
        }

        string path = PathFor(ip, ns);
        try
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            var payload = new JsonObject
            {
                ["_saved_at"] = now,
                ["data"] = data.DeepClone(),
            };
            File.WriteAllText(path, payload.ToJsonString(), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Raw-text cache sibling (exit lists, range feeds). Same TTL rules.</summary>
    public static string? GetText(string key, int ttlSeconds, string ns = "")
    {
        if (ttlSeconds <= 0)
        {
            return null;
        }

        string path = PathFor(key, "text:" + ns);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length < 2
                || !double.TryParse(lines[0],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double saved))
            {
                return null;
            }

            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (now - saved > ttlSeconds)
            {
                return null;
            }

            return string.Join("\n", lines[1..]);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
            || ex is FormatException)
        {
            return null;
        }
    }

    public static void PutText(string key, string text, int ttlSeconds, string ns = "")
    {
        if (ttlSeconds <= 0)
        {
            return;
        }

        string lowered = text.ToLowerInvariant();
        if (lowered.Contains("api_key") || lowered.Contains("apikey") || lowered.Contains("bearer"))
        {
            return;
        }

        string path = PathFor(key, "text:" + ns);
        try
        {
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            File.WriteAllText(
                path,
                now.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + text,
                Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
    }
}
