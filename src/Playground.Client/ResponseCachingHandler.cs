using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Playground.Client;

/// <summary>
/// HTTP-layer response cache that <b>actually works</b> with Refit.
///
/// <para>
/// Replaces <c>Polly.CacheAsync&lt;HttpResponseMessage&gt;</c>, which has two latent bugs when
/// wired up through <c>Microsoft.Extensions.Http.Polly</c>:
/// <list type="number">
///   <item><description>
///     The Polly cache policy keys entries by <c>Context.OperationKey</c>, but
///     <c>PolicyHttpMessageHandler</c> never populates it, so every call misses.
///   </description></item>
///   <item><description>
///     Even with an <c>OperationKey</c>, the policy caches the same
///     <see cref="HttpResponseMessage"/> instance — Refit disposes its
///     <see cref="HttpResponseMessage.Content"/> after deserializing the first response,
///     so the next cache hit throws <see cref="ObjectDisposedException"/>.
///   </description></item>
/// </list>
/// </para>
///
/// <para>
/// This handler avoids both problems by storing a value-typed snapshot —
/// status code, headers, content bytes, content-type — and constructing a fresh
/// <see cref="HttpResponseMessage"/> on every cache hit. Refit then owns and disposes
/// that fresh instance as usual.
/// </para>
///
/// <para>Only successful GET responses (2xx) are cached.</para>
/// </summary>
public sealed class ResponseCachingHandler : DelegatingHandler
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;
    private readonly ILogger<ResponseCachingHandler> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public ResponseCachingHandler(
        IMemoryCache cache,
        TimeSpan ttl,
        ILogger<ResponseCachingHandler> logger)
    {
        _cache = cache;
        _ttl = ttl;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        var cacheKey = $"{request.Method} {request.RequestUri.AbsoluteUri}";

        if (_cache.TryGetValue<ResponseSnapshot>(cacheKey, out var cached) && cached is not null)
        {
            _logger.LogInformation("[cache] HIT  key={CacheKey}", cacheKey);
            return cached.ToHttpResponseMessage(request);
        }

        // Single-flight: collapse concurrent misses for the same key onto one upstream call.
        var gate = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock — another caller may have populated the cache.
            if (_cache.TryGetValue<ResponseSnapshot>(cacheKey, out cached) && cached is not null)
            {
                _logger.LogInformation("[cache] HIT (after wait)  key={CacheKey}", cacheKey);
                return cached.ToHttpResponseMessage(request);
            }

            _logger.LogInformation("[cache] MISS key={CacheKey}", cacheKey);
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var snapshot = await ResponseSnapshot.FromAsync(response, cancellationToken).ConfigureAwait(false);
                _cache.Set(cacheKey, snapshot, _ttl);

                // Return a fresh response built from the snapshot so the original
                // response (now buffered) and the cached snapshot are independent.
                response.Dispose();
                return snapshot.ToHttpResponseMessage(request);
            }

            return response;
        }
        finally
        {
            _ = gate.Release();
        }
    }

    [SuppressMessage("Performance", "CA1812", Justification = "Instantiated via cache entries.")]
    private sealed record ResponseSnapshot(
        System.Net.HttpStatusCode StatusCode,
        string? ReasonPhrase,
        IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>> ResponseHeaders,
        IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>> ContentHeaders,
        byte[] Body)
    {
        public HttpResponseMessage ToHttpResponseMessage(HttpRequestMessage request)
        {
            var response = new HttpResponseMessage(StatusCode)
            {
                ReasonPhrase = ReasonPhrase,
                RequestMessage = request,
                Content = new ByteArrayContent(Body)
            };

            foreach (var header in ResponseHeaders)
            {
                _ = response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            foreach (var header in ContentHeaders)
            {
                _ = response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return response;
        }

        public static async Task<ResponseSnapshot> FromAsync(
            HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            static IReadOnlyList<KeyValuePair<string, IReadOnlyList<string>>> Copy(
                IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
                => headers
                    .Select(h => new KeyValuePair<string, IReadOnlyList<string>>(h.Key, h.Value.ToList()))
                    .ToList();

            return new ResponseSnapshot(
                response.StatusCode,
                response.ReasonPhrase,
                Copy(response.Headers),
                Copy(response.Content.Headers),
                body);
        }
    }
}
