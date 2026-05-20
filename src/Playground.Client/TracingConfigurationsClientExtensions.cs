using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using Playground.Contracts;
using Refit;

namespace Playground.Client;

/// <summary>
/// Registers the Refit client with response caching + Polly retry, mirroring the spirit of
/// <c>TraceControlPlaneClientExtensions.AddTraceControlPlaneClient</c> but with a correct cache
/// implementation (see <see cref="ResponseCachingHandler"/> for why the original Polly cache
/// wiring was a no-op).
///
/// <para>
/// Resulting <c>HttpClient</c> handler chain (outermost first):
/// </para>
///
/// <code>
/// HttpClient
///   → PipelineLoggingHandler("0-outer")
///     → ResponseCachingHandler                    (short-circuits on cache HIT)
///       → PipelineLoggingHandler("1-after-cache")
///         → Polly retry policy                    (re-enters this branch on each retry)
///           → PipelineLoggingHandler("2-after-retry")
///             → primary HttpClientHandler (socket)
/// </code>
/// </summary>
public static class TracingConfigurationsClientExtensions
{
    public static IServiceCollection AddTracingConfigurationsClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(TracingConfigurationsClientOptions.SectionName)
            .Get<TracingConfigurationsClientOptions>() ?? new TracingConfigurationsClientOptions();

        if (!options.Enabled)
        {
            services.AddSingleton<ITracingConfigurationsApi, NoOpTracingConfigurationsClient>();
            return services;
        }

        if (string.IsNullOrWhiteSpace(options.BaseAddress))
        {
            throw new InvalidOperationException(
                $"'{TracingConfigurationsClientOptions.SectionName}:" +
                $"{nameof(TracingConfigurationsClientOptions.BaseAddress)}' must be configured when Enabled is true.");
        }

        services.AddMemoryCache();

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                options.RetryCount,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(options.RetryBackoffBaseSeconds, retryAttempt)),
                onRetry: (outcome, delay, attempt, _) =>
                {
                    // Polly's own retry log — visible in addition to the handler logs.
                    Console.WriteLine(
                        $"[polly:retry] attempt {attempt} after {delay.TotalMilliseconds:F0} ms " +
                        $"due to {(outcome.Exception?.GetType().Name ?? outcome.Result?.StatusCode.ToString())}");
                });

        var clientBuilder = services
            .AddRefitClient<ITracingConfigurationsApi>()
            .ConfigureHttpClient(httpClient =>
            {
                httpClient.BaseAddress = new Uri(options.BaseAddress);
            })
            // Order matters: AddHttpMessageHandler / AddPolicyHandler are *appended* to the
            // chain, so the first registration sits outermost (closest to HttpClient).
            .AddHttpMessageHandler(sp => new PipelineLoggingHandler(
                sp.GetRequiredService<ILogger<PipelineLoggingHandler>>(), "0-outer"));

        if (options.CacheTtl > TimeSpan.Zero)
        {
            clientBuilder.AddHttpMessageHandler(sp => new ResponseCachingHandler(
                sp.GetRequiredService<IMemoryCache>(),
                options.CacheTtl,
                sp.GetRequiredService<ILogger<ResponseCachingHandler>>()));
        }

        clientBuilder
            .AddHttpMessageHandler(sp => new PipelineLoggingHandler(
                sp.GetRequiredService<ILogger<PipelineLoggingHandler>>(), "1-after-cache"))
            .AddPolicyHandler(retryPolicy)
            .AddHttpMessageHandler(sp => new PipelineLoggingHandler(
                sp.GetRequiredService<ILogger<PipelineLoggingHandler>>(), "2-after-retry"));

        return services;
    }
}
