using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Playground.Client;

/// <summary>
/// A <see cref="DelegatingHandler"/> that logs every request/response that passes through it.
/// Inserted at multiple points in the handler chain so you can see, for a single Refit call:
///   1. what the outermost layer sees,
///   2. what the cache-policy layer sees,
///   3. what the retry-policy layer sees (each retry attempt is a separate trip through here),
///   4. what the innermost socket-facing layer sees.
/// </summary>
public sealed class PipelineLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PipelineLoggingHandler> _logger;
    private readonly string _tag;

    public PipelineLoggingHandler(ILogger<PipelineLoggingHandler> logger, string tag)
    {
        _logger = logger;
        _tag = tag;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var requestId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")[..8];

        _logger.LogInformation(
            "[{Tag}] → {Method} {Uri}  (trace={TraceId})",
            _tag, request.Method, request.RequestUri, requestId);

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            _logger.LogInformation(
                "[{Tag}] ← {StatusCode} {ReasonPhrase}  ({ElapsedMs} ms) {Method} {Uri}",
                _tag, (int)response.StatusCode, response.ReasonPhrase, sw.ElapsedMilliseconds,
                request.Method, request.RequestUri);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(
                ex, "[{Tag}] ✗ {ExceptionType} after {ElapsedMs} ms  {Method} {Uri}",
                _tag, ex.GetType().Name, sw.ElapsedMilliseconds, request.Method, request.RequestUri);
            throw;
        }
    }
}
