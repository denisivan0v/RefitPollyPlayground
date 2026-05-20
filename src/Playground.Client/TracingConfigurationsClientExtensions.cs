using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Caching.Memory;
using Polly.Extensions.Http;
using Playground.Contracts;
using Refit;

namespace Playground.Client;

/// <summary>
/// Registers the Refit client + Polly cache & retry policies, mirroring
/// <c>TraceControlPlaneClientExtensions.AddTraceControlPlaneClient</c>.
///
/// Resulting <c>HttpClient</c> handler chain (outermost first):
///
///   <c>HttpClient</c>
///     → <c>PipelineLoggingHandler("0-outer")</c>
///       → Polly cache policy             (returns cached response without going further)
///         → <c>PipelineLoggingHandler("1-after-cache")</c>
///           → Polly retry policy         (re-enters this branch on each retry)
///             → <c>PipelineLoggingHandler("2-after-retry")</c>
///               → primary <c>HttpClientHandler</c> (socket)
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

        services
            .AddRefitClient<ITracingConfigurationsApi>()
            .ConfigureHttpClient(httpClient =>
            {
                httpClient.BaseAddress = new Uri(options.BaseAddress);
            })
            // Order matters: AddHttpMessageHandler / AddPolicyHandler are *appended* to the
            // chain, so the first registration sits outermost (closest to HttpClient).
            .AddHttpMessageHandler(sp => new PipelineLoggingHandler(
                sp.GetRequiredService<ILogger<PipelineLoggingHandler>>(), "0-outer"))
            // OPTIONAL: insert PollyCacheKeyHandler *before* the cache policy so the
            // Context.OperationKey is set when the policy looks for a cache hit.
            // Without this, Polly's CacheAsync never hits — see PollyCacheKeyHandler XML docs.
            .AddHttpMessageHandlerWhen(options.EnableCacheKeyFix, () => new PollyCacheKeyHandler())
            .AddPolicyHandler((sp, _) =>
            {
                var memoryCache = sp.GetRequiredService<IMemoryCache>();
                var cacheProvider = new MemoryCacheProvider(memoryCache);
                return Policy.CacheAsync<HttpResponseMessage>(cacheProvider, options.CacheTtl);
            })
            .AddHttpMessageHandler(sp => new PipelineLoggingHandler(
                sp.GetRequiredService<ILogger<PipelineLoggingHandler>>(), "1-after-cache"))
            .AddPolicyHandler(retryPolicy)
            .AddHttpMessageHandler(sp => new PipelineLoggingHandler(
                sp.GetRequiredService<ILogger<PipelineLoggingHandler>>(), "2-after-retry"));

        return services;
    }

    private static IHttpClientBuilder AddHttpMessageHandlerWhen(
        this IHttpClientBuilder builder, bool condition, Func<DelegatingHandler> factory)
        => condition ? builder.AddHttpMessageHandler(factory) : builder;
}
