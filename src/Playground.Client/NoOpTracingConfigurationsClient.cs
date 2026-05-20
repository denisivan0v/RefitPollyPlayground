using Microsoft.Extensions.Logging;
using Playground.Contracts;

namespace Playground.Client;

/// <summary>
/// No-op fallback when <see cref="TracingConfigurationsClientOptions.Enabled"/> is false.
/// Mirrors <c>NoOpTraceControlPlaneClient</c> in the real repo.
/// </summary>
public sealed class NoOpTracingConfigurationsClient : ITracingConfigurationsApi
{
    public NoOpTracingConfigurationsClient(ILogger<NoOpTracingConfigurationsClient> logger)
        => logger.LogInformation("Tracing configurations client is disabled (no-op).");

    public Task<TracingConfigurationListResponse> GetByGenevaLogAccountAsync(
        string logsNamespace, string? endpoint = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new TracingConfigurationListResponse());

    public Task<TracingConfigurationListResponse> GetByResourceIdAsync(
        string appInsightsResourceId, CancellationToken cancellationToken = default)
        => Task.FromResult(new TracingConfigurationListResponse());
}
