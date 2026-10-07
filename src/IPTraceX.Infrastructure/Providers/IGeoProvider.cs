using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>Provider seam: one lookup in, one normalized result out.</summary>
public interface IGeoProvider
{
    string Name { get; }
    int[] SupportedIpVersions { get; }
    Task<GeoResult> LookupAsync(string ip, CancellationToken cancellationToken = default);
}
