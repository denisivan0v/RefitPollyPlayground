// AppHost Program.cs — uses the path-based AddProject overload so we don't need
// the Aspire workload's source-generator to be installed.
using System.IO;

var builder = DistributedApplication.CreateBuilder(args);

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

var server = builder
    .AddProject("server", Path.Combine(repoRoot, "src", "Playground.Server", "Playground.Server.csproj"))
    .WithHttpEndpoint(port: 5050, name: "http");

builder
    .AddProject("console-client",
        Path.Combine(repoRoot, "src", "Playground.ConsoleClient", "Playground.ConsoleClient.csproj"))
    .WithEnvironment("TracingConfigurationsClient__Enabled", "true")
    .WithEnvironment("TracingConfigurationsClient__BaseAddress", server.GetEndpoint("http"))
    .WithEnvironment("TracingConfigurationsClient__CacheTtl", "00:00:30");

await builder.Build().RunAsync().ConfigureAwait(false);
