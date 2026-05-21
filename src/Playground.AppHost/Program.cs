var builder = DistributedApplication.CreateBuilder(args);

var server = builder
    .AddProject<Projects.Playground_Server>("server")
    .WithHttpEndpoint(port: 5050, name: "http");

builder
    .AddProject<Projects.Playground_ConsoleClient>("console-client")
    .WithEnvironment("TracingConfigurationsClient__Enabled", "true")
    .WithEnvironment("TracingConfigurationsClient__BaseAddress", server.GetEndpoint("http"))
    .WithEnvironment("TracingConfigurationsClient__CacheTtl", "00:00:30");

await builder.Build().RunAsync().ConfigureAwait(false);
