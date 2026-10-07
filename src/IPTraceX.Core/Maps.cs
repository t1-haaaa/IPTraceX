using System.Diagnostics;
using System.Globalization;

namespace IPTraceX.Core;

/// <summary>
/// Google Maps helpers -- validation first, URL second. The URL is always
/// built locally from validated doubles; provider URLs never reach a shell.
/// </summary>
public static class Maps
{
    public static double? ToDouble(object? value)
    {
        if (value is null || value is bool)
        {
            return null;
        }

        try
        {
            double result = value switch
            {
                double d => d,
                float f => f,
                long l => l,
                int i => i,
                string s => double.Parse(s.Trim(), CultureInfo.InvariantCulture),
                System.Text.Json.JsonElement e => e.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Number => e.GetDouble(),
                    System.Text.Json.JsonValueKind.String
                        => double.Parse(e.GetString()!.Trim(), CultureInfo.InvariantCulture),
                    _ => double.NaN,
                },
                _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            };
            if (double.IsNaN(result) || double.IsInfinity(result))
            {
                return null;
            }

            return result;
        }
        catch (Exception ex) when (ex is FormatException || ex is OverflowException
            || ex is InvalidCastException || ex is ArgumentException)
        {
            return null;
        }
    }

    public static bool IsValidCoordinate(object? latitude, object? longitude)
    {
        double? lat = ToDouble(latitude);
        double? lon = ToDouble(longitude);
        if (!lat.HasValue || !lon.HasValue)
        {
            return false;
        }

        return lat.Value >= -90.0 && lat.Value <= 90.0
            && lon.Value >= -180.0 && lon.Value <= 180.0;
    }

    public static string? BuildMapsUrl(object? latitude, object? longitude)
    {
        if (!IsValidCoordinate(latitude, longitude))
        {
            return null;
        }

        double lat = ToDouble(latitude)!.Value;
        double lon = ToDouble(longitude)!.Value;
        return string.Create(CultureInfo.InvariantCulture,
            $"https://www.google.com/maps?q={lat},{lon}");
    }

    /// <summary>Open the URL safely (no shell). Returns (opened, message).</summary>
    public static (bool Opened, string Message) OpenInBrowser(string url)
    {
        if (!url.StartsWith("https://www.google.com/maps?q=", StringComparison.Ordinal))
        {
            return (false, "Refusing to open an unexpected URL.");
        }

        string? opener = FindOnPath("xdg-open")
            ?? FindOnPath("gio")
            ?? FindOnPath("sensible-browser")
            ?? FindOnPath("x-www-browser");
        if (opener is null)
        {
            return (false, "Could not open browser automatically.");
        }

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = opener;
            process.StartInfo.ArgumentList.Add(url);
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            if (!process.WaitForExit(10_000))
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                }
            }

            return (true, "Browser opened.");
        }
        catch (Exception)
        {
            return (false, "Could not open browser automatically.");
        }
    }

    private static string? FindOnPath(string name)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (string dir in pathEnv.Split(Path.PathSeparator))
        {
            if (dir.Length == 0)
            {
                continue;
            }

            try
            {
                string candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
            {
                continue;
            }
        }

        return null;
    }
}
