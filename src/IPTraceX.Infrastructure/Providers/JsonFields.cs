using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>Shared JSON field helpers. Never lets provider data crash the app.</summary>
internal static class JsonFields
{
    public static string? Str(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(name, out JsonElement element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => BlankToNull(element.GetString()),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public static double? Num(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(name, out JsonElement element))
        {
            return null;
        }

        double? result = element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out double d) ? d : null,
            JsonValueKind.String => double.TryParse(element.GetString()?.Trim(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double s) ? s : null,
            _ => null,
        };
        if (result.HasValue && (double.IsNaN(result.Value) || double.IsInfinity(result.Value)))
        {
            return null;
        }

        return result;
    }

    public static string? BlankToNull(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
