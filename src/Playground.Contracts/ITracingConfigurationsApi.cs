using Refit;

namespace Playground.Contracts;

/// <summary>
/// Refit interface — mirrors <c>ITraceControlPlaneClient</c> in the real repo.
/// Refit generates the HTTP implementation at DI registration time
/// (via <c>AddRefitClient&lt;ITracingConfigurationsApi&gt;()</c>).
/// </summary>
public interface ITracingConfigurationsApi
{
    /// <summary>
    /// GET /tracing-configurations?namespace={ns}&amp;endpoint={endpoint}
    /// </summary>
    [Get("/tracing-configurations")]
    Task<TracingConfigurationListResponse> GetByGenevaLogAccountAsync(
        [AliasAs("namespace")] string logsNamespace,
        [AliasAs("endpoint")] string? endpoint = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// GET /tracing-configurations?appInsightsResourceId={id}
    /// </summary>
    [Get("/tracing-configurations")]
    Task<TracingConfigurationListResponse> GetByResourceIdAsync(
        [AliasAs("appInsightsResourceId")] string appInsightsResourceId,
        CancellationToken cancellationToken = default);
}
