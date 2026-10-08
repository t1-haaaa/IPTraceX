using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>
/// Public footprint via GitHub (free). Two tiers, both labeled honestly:
/// - handle page existence (no key): WEAK possible-handle signal only.
/// - commit authorship search (optional IPTraceX_GITHUB_TOKEN):
///   public commits authored by the email = confirmed public match.
/// Never claims identity from a weak correlation.
/// </summary>
public sealed class GitHubFootprintProvider : IEmailProvider
{
    public ProviderDescriptor Descriptor { get; } = new(
        "github-footprint",
        "GitHub Footprint",
        ProviderCategory.Footprint,
        [4, 6],
        false,
        "Optional IPTraceX_GITHUB_TOKEN (commit search needs auth).",
        "Unauthenticated: 60/hr + search limits; authed higher.");

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;

    public GitHubFootprintProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
    {
        _timeoutSeconds = timeoutSeconds;
        _fetcher = fetcher ?? new HttpJsonClient();
    }

    public async Task<EmailEvidence> InvestigateAsync(
        EmailTarget target, EmailContext context, CancellationToken cancellationToken = default)
    {
        var matches = new List<FootprintMatch>();
        try
        {
            // Tier 1 (no key): does a public handle page exist?
            try
            {
                JsonElement? user = await _fetcher.FetchAsync(
                    $"https://api.github.com/users/{Uri.EscapeDataString(target.Local)}",
                    _timeoutSeconds, null, cancellationToken).ConfigureAwait(false);
                if (user is { } userEl && userEl.ValueKind == JsonValueKind.Object
                    && userEl.TryGetProperty("login", out JsonElement loginEl)
                    && loginEl.ValueKind == JsonValueKind.String)
                {
                    string login = loginEl.GetString()!;
                    bool exact = login.Equals(target.Local, StringComparison.OrdinalIgnoreCase);
                    matches.Add(new FootprintMatch(
                        "GitHub",
                        $"https://github.com/{login}",
                        userEl.TryGetProperty("name", out JsonElement nameEl)
                            && nameEl.ValueKind == JsonValueKind.String
                            ? nameEl.GetString() ?? login : login,
                        "handle-exists",
                        target.Local,
                        exact ? "LOW" : "LOW",
                        Descriptor.Id,
                        DateTimeOffset.UtcNow));
                }
            }
            catch (NotFoundException)
            {
                // No such handle: not evidence of anything.
            }

            // Tier 2 (token only): public commits authored by this email.
            if (!string.IsNullOrWhiteSpace(context.GitHubToken))
            {
                try
                {
                    var headers = new Dictionary<string, string>
                    {
                        ["Authorization"] = "Bearer " + context.GitHubToken.Trim(),
                        ["Accept"] = "application/vnd.github+json",
                    };
                    JsonElement? search = await _fetcher.FetchAsync(
                        "https://api.github.com/search/commits?q="
                        + Uri.EscapeDataString($"author-email:{target.Normalized}"),
                        _timeoutSeconds, headers, cancellationToken).ConfigureAwait(false);
                    if (search is { } searchEl && searchEl.ValueKind == JsonValueKind.Object
                        && searchEl.TryGetProperty("items", out JsonElement items)
                        && items.ValueKind == JsonValueKind.Array)
                    {
                        var repos = new List<string>();
                        foreach (JsonElement item in items.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object)
                            {
                                continue;
                            }

                            if (item.TryGetProperty("repository", out JsonElement repo)
                                && repo.ValueKind == JsonValueKind.Object
                                && repo.TryGetProperty("full_name", out JsonElement fullName)
                                && fullName.ValueKind == JsonValueKind.String
                                && fullName.GetString() is string repoName
                                && !repos.Contains(repoName, StringComparer.OrdinalIgnoreCase))
                            {
                                repos.Add(repoName);
                                if (repos.Count >= 3)
                                {
                                    break;
                                }
                            }
                        }

                        if (repos.Count != 0)
                        {
                            matches.Add(new FootprintMatch(
                                "GitHub",
                                $"https://github.com/search?q={Uri.EscapeDataString("author-email:" + target.Normalized)}&type=commits",
                                "Public commits authored by " + target.Normalized,
                                "commit-authorship",
                                target.Normalized,
                                "MEDIUM",
                                Descriptor.Id,
                                DateTimeOffset.UtcNow));
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Token-gated tier failing never fails the provider.
                }
            }

            return new FootprintEvidence(Descriptor.Id, true, null, [.. matches]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            string message = ex is TraceXException tx ? tx.Message : ex.GetType().Name;
            return new FootprintEvidence(Descriptor.Id, false, message, []);
        }
    }
}
