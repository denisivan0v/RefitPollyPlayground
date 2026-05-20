using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Playground.Client;
using Playground.Contracts;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss.fff ";
});

builder.Services.AddTracingConfigurationsClient(builder.Configuration);

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
var api = host.Services.GetRequiredService<ITracingConfigurationsApi>();

logger.LogInformation("=== Scenario 1: GetByGenevaLogAccount (cache MISS — first call) ===");
await PrintAsync(api.GetByGenevaLogAccountAsync("AzureMonitor", endpoint: "Diagnostics PROD"));

logger.LogInformation("=== Scenario 2: GetByGenevaLogAccount with same args (cache HIT) ===");
await PrintAsync(api.GetByGenevaLogAccountAsync("AzureMonitor", endpoint: "Diagnostics PROD"));

logger.LogInformation("=== Scenario 3: GetByGenevaLogAccount with different args (cache MISS — different cache key) ===");
await PrintAsync(api.GetByGenevaLogAccountAsync("AzureMonitor", endpoint: "Diagnostics INT"));

logger.LogInformation("=== Scenario 4: GetByResourceId — valid ARM ID ===");
await PrintAsync(api.GetByResourceIdAsync(
    "/subscriptions/11111111-1111-1111-1111-111111111111/resourcegroups/rg-prod/providers/microsoft.insights/components/app-prod"));

logger.LogInformation("=== Scenario 5: GetByResourceId — INVALID ARM ID (expect 400 from server; Polly retry triggers ONLY on transient errors so this should NOT retry) ===");
try
{
    await PrintAsync(api.GetByResourceIdAsync("not-a-valid-id"));
}
catch (Refit.ApiException ex)
{
    logger.LogWarning(
        "ApiException as expected: status={Status} content={Content}",
        (int)ex.StatusCode, ex.Content);
}

logger.LogInformation("=== Done. Press Ctrl+C to exit (kept alive so traces flush). ===");

await host.RunAsync().ConfigureAwait(false);

static async Task PrintAsync(Task<TracingConfigurationListResponse> task)
{
    var response = await task.ConfigureAwait(false);
    Console.WriteLine($"   ↳ result: {response.Value.Count} configuration(s)");
    foreach (var item in response.Value)
    {
        Console.WriteLine($"       • {item.AmwId} — {item.DisplayName} (enabled={item.EnableTracing})");
    }
}
