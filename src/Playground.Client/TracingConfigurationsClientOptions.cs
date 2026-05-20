namespace Playground.Client;

/// <summary>
/// Options bound from the <c>"TracingConfigurationsClient"</c> configuration section.
/// Mirrors <c>TraceControlPlaneClientOptions</c> in the real repo.
/// </summary>
public sealed class TracingConfigurationsClientOptions
{
    public const string SectionName = "TracingConfigurationsClient";

    /// <summary>When false, all calls return empty results without hitting the network.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Required when <see cref="Enabled"/> is true. May be a service:// URI when used with Aspire service discovery.</summary>
    public string BaseAddress { get; set; } = string.Empty;

    /// <summary>
    /// Cache TTL for successful GET responses. Set to <see cref="TimeSpan.Zero"/> to disable
    /// the cache entirely (skips inserting <c>ResponseCachingHandler</c> into the pipeline).
    /// Defaults to 30 seconds (small for demo purposes).
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Polly retry count for transient HTTP errors.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Exponential backoff base in seconds.</summary>
    public int RetryBackoffBaseSeconds { get; set; } = 2;
}
