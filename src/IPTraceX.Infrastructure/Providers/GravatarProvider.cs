using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>
/// Public avatar discovery via Gravatar (free, no key).
/// Only publicly associated avatars are reported; a 404 means UNKNOWN
/// ("public image not found") — never "no Google account".
/// </summary>
public sealed class GravatarProvider : IEmailProvider
{
    public ProviderDescriptor Descriptor { get; } = new(
        "gravatar",
        "Gravatar",
        ProviderCategory.Avatar,
        [4, 6],
        false,
        "No key required.",
        "Tiny profile lookups; fair use.");

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;

    public GravatarProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
    {
        _timeoutSeconds = timeoutSeconds;
        _fetcher = fetcher ?? new HttpJsonClient();
    }

    public async Task<EmailEvidence> InvestigateAsync(
        EmailTarget target, EmailContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            string hash = Md5Hex(target.Normalized.Trim().ToLowerInvariant());
            JsonElement? payload;
            try
            {
                payload = await _fetcher.FetchAsync(
                    $"https://en.gravatar.com/{hash}.json",
                    _timeoutSeconds, null, cancellationToken).ConfigureAwait(false);
            }
            catch (NotFoundException)
            {
                return new AvatarEvidence(Descriptor.Id, true, null, false, null, null);
            }

            if (payload is not { } root || root.ValueKind != JsonValueKind.Object)
            {
                return new AvatarEvidence(Descriptor.Id, true, null, false, null, null);
            }

            if (!root.TryGetProperty("entry", out JsonElement entries)
                || entries.ValueKind != JsonValueKind.Array
                || entries.GetArrayLength() == 0)
            {
                return new AvatarEvidence(Descriptor.Id, true, null, false, null, null);
            }

            JsonElement entry = entries[0];
            string? profileUrl = entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("profileUrl", out JsonElement profileEl)
                && profileEl.ValueKind == JsonValueKind.String
                ? profileEl.GetString()?.Trim()
                : null;
            string? displayName = entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("displayName", out JsonElement nameEl)
                && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString()?.Trim()
                : null;
            if (string.IsNullOrEmpty(profileUrl))
            {
                return new AvatarEvidence(Descriptor.Id, true, null, false, null, null);
            }

            return new AvatarEvidence(Descriptor.Id, true, null, true, profileUrl, displayName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string message = ex is TraceXException tx ? tx.Message : ex.GetType().Name;
            return new AvatarEvidence(Descriptor.Id, false, message, false, null, null);
        }
    }

    public static string Md5Hex(string normalizedEmail)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
