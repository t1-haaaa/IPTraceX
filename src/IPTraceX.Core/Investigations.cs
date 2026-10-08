using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace IPTraceX.Core;

/// <summary>Investigation identity: IPX-YYYYMMDD-XXXXXX (X = A-Z0-9).</summary>
public static class InvestigationId
{
    private static readonly Regex ValidFormat =
        new(@"^IPX-\d{8}-[A-Z0-9]{6}$", RegexOptions.Compiled);

    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string New()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(6);
        char[] suffix = new char[6];
        for (int i = 0; i < 6; i++)
        {
            suffix[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return $"IPX-{DateTimeOffset.UtcNow:yyyyMMdd}-{new string(suffix)}";
    }

    public static bool IsValid(string? id)
        => !string.IsNullOrWhiteSpace(id) && ValidFormat.IsMatch(id);

    public static string RequireValid(string? id)
    {
        if (!IsValid(id))
        {
            throw new UsageException(
                $"Invalid investigation ID: '{id}'. Expected format IPX-YYYYMMDD-XXXXXX.");
        }

        return id!;
    }
}

/// <summary>One row of the evidence matrix.</summary>
public sealed record EvidenceRow(
    string Field,
    string Provider,
    string Value,
    string Status);

/// <summary>Evidence matrix statuses.</summary>
public static class EvidenceStatus
{
    public const string Support = "SUPPORT";
    public const string Conflict = "CONFLICT";
    public const string Missing = "MISSING";
    public const string Error = "ERROR";
    public const string Stale = "STALE";
    public const string Cached = "CACHED";
}

/// <summary>Source reliability tiers (displayed, never hides evidence).</summary>
public static class ReliabilityTier
{
    public const string High = "HIGH";
    public const string Medium = "MEDIUM";
    public const string Low = "LOW";
    public const string Unknown = "UNKNOWN";
}

/// <summary>Explained confidence for one field.</summary>
public sealed record ConfidenceDetail(
    string Field,
    string? Value,
    string Level,
    int Agreeing,
    int Successful,
    string Reason,
    string[] SupportingProviders,
    string[] ConflictingProviders);

/// <summary>First-class investigation: one analyzed target, frozen in time.</summary>
public sealed record Investigation(
    string Id,
    DateTimeOffset TimestampUtc,
    string ToolVersion,
    string Target,
    string TargetType,
    IntelligenceProfile Profile,
    IReadOnlyList<string> Errors,
    string SchemaVersion)
{
    public const string CurrentSchema = "2.1";
}
