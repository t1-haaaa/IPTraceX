using System.Net;
using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure;

/// <summary>Fetches provider JSON. Swappable in tests (no network in unit tests).</summary>
public interface IGeoJsonFetcher
{
    Task<JsonElement?> FetchAsync(
        string url,
        double timeoutSeconds,
        IDictionary<string, string>? extraHeaders = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Minimal HTTPS JSON client. One shared HttpClient (proper lifetime);
/// per-request timeout via CancellationTokenSource. Limited retry for
/// transient failures only (network blips, HTTP 5xx) -- never 401/403/404/429.
/// </summary>
public sealed class HttpJsonClient : IGeoJsonFetcher
{
    private static readonly HttpClient Shared = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
    });

    static HttpJsonClient()
    {
        Shared.DefaultRequestHeaders.UserAgent.ParseAdd("IPTraceX/1.0.0 (+https://github.com/t1-haaaa/IPTraceX)");
        Shared.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<JsonElement?> FetchAsync(
        string url,
        double timeoutSeconds,
        IDictionary<string, string>? extraHeaders = null,
        CancellationToken cancellationToken = default)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new NetworkException("Refusing non-HTTPS provider URL.");
        }

        Exception? last = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (extraHeaders is not null)
                {
                    foreach (var (key, value) in extraHeaders)
                    {
                        request.Headers.TryAddWithoutValidation(key, value);
                    }
                }

                using HttpResponseMessage response =
                    await Shared.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token)
                        .ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if (status == 429)
                {
                    throw new RateLimitException(
                        "API rate limit reached. Please wait and try again later.");
                }

                if (status is 401 or 403)
                {
                    throw new AuthException($"Provider authentication failed (HTTP {status}).");
                }

                if (status == 404)
                {
                    throw new NotFoundException("Provider has no data for this IP (HTTP 404).");
                }

                if (status >= 500 && status <= 599)
                {
                    last = new NetworkException($"Provider unavailable (HTTP {status}).");
                    await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(raw);
                    return doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    throw new BadResponseException("Provider returned malformed JSON.");
                }
            }
            catch (RateLimitException)
            {
                throw;
            }
            catch (AuthException)
            {
                throw;
            }
            catch (NotFoundException)
            {
                throw;
            }
            catch (BadResponseException)
            {
                throw;
            }
            catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
            {
                int code = (int)ex.StatusCode.Value;
                if (code == 429)
                {
                    throw new RateLimitException(
                        "API rate limit reached. Please wait and try again later.");
                }

                if (code is 401 or 403)
                {
                    throw new AuthException($"Provider authentication failed (HTTP {code}).");
                }

                if (code == 404)
                {
                    throw new NotFoundException("Provider has no data for this IP (HTTP 404).");
                }

                if (code >= 500 && code <= 599)
                {
                    last = new NetworkException($"Provider unavailable (HTTP {code}).");
                    await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                throw new NetworkException($"Provider request failed (HTTP {code}).");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Our own timeout fired (not the caller's cancellation).
                if (attempt >= 2)
                {
                    throw new ProviderTimeoutException("Provider request timed out.");
                }

                await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                last = ex;
                if (attempt >= 2)
                {
                    throw new NetworkException($"Network error: {ex.Message}.");
                }

                await DelayAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new NetworkException($"Network error: {last?.Message}.");
    }

    // No Thread.Sleep for network waits -- async delay only.
    private static Task DelayAsync(int attempt, CancellationToken ct)
        => Task.Delay(TimeSpan.FromSeconds(0.5 * (attempt + 1)), ct);
}
