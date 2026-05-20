using Playground.Contracts;

namespace Playground.Server;

/// <summary>
/// Hard-coded sample data — the equivalent of <c>TracingConfigurationService</c> in the real repo
/// but reading from an in-memory list instead of a mounted ConfigMap.
/// </summary>
public sealed class InMemoryTracingConfigurationStore
{
    private static readonly IReadOnlyList<Entry> Data =
    [
        new Entry(
            Namespace: "AzureMonitor",
            Endpoint:  "Diagnostics PROD",
            AppInsightsResourceId: "/subscriptions/11111111-1111-1111-1111-111111111111/resourcegroups/rg-prod/providers/microsoft.insights/components/app-prod",
            Response: new TracingConfigurationResponse
            {
                AmwId = "amw-prod-eastus",
                DisplayName = "Production EastUS",
                EnableTracing = true,
                StorageResourceId = "/subscriptions/11111111-1111-1111-1111-111111111111/resourcegroups/rg-prod/providers/microsoft.storage/storageaccounts/stprodeastus",
                AzureStorageBlobEndpoint = "https://stprodeastus.blob.core.windows.net",
                TraceDurationWindowInSeconds = 60,
                LastChangedTimeStamp = DateTime.UtcNow.AddDays(-2),
                IsDeleted = false
            }),
        new Entry(
            Namespace: "AzureMonitor",
            Endpoint:  "Diagnostics INT",
            AppInsightsResourceId: "/subscriptions/22222222-2222-2222-2222-222222222222/resourcegroups/rg-int/providers/microsoft.insights/components/app-int",
            Response: new TracingConfigurationResponse
            {
                AmwId = "amw-int-westus",
                DisplayName = "Integration WestUS",
                EnableTracing = true,
                StorageResourceId = "/subscriptions/22222222-2222-2222-2222-222222222222/resourcegroups/rg-int/providers/microsoft.storage/storageaccounts/stintwestus",
                AzureStorageBlobEndpoint = "https://stintwestus.blob.core.windows.net",
                TraceDurationWindowInSeconds = 30,
                LastChangedTimeStamp = DateTime.UtcNow.AddHours(-12),
                IsDeleted = false
            })
    ];

    public TracingConfigurationListResponse GetByGenevaLogAccount(string? endpoint, string @namespace)
        => new()
        {
            Value = Data
                .Where(e => e.Namespace.StartsWith(@namespace, StringComparison.OrdinalIgnoreCase))
                .Where(e => endpoint is null || e.Endpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Response)
                .ToList()
        };

    public TracingConfigurationListResponse GetByAppInsightsResourceId(string appInsightsResourceId)
        => new()
        {
            Value = Data
                .Where(e => e.AppInsightsResourceId.Equals(appInsightsResourceId, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Response)
                .ToList()
        };

    private sealed record Entry(
        string Namespace,
        string Endpoint,
        string AppInsightsResourceId,
        TracingConfigurationResponse Response);
}
