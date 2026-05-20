using Polly;

namespace Playground.Client;

/// <summary>
/// Sets <c>Context.OperationKey</c> on the Polly <see cref="Context"/> for the current request
/// so that <c>Policy.CacheAsync</c> downstream can actually hit its cache.
///
/// <para>
/// Why is this needed? <c>Microsoft.Extensions.Http.Polly</c>'s
/// <c>PolicyHttpMessageHandler</c> creates a fresh <see cref="Context"/> for every request
/// but does **not** populate <c>OperationKey</c>. Polly's cache policy keys cache entries
/// by <c>Context.OperationKey</c> — when it's empty/null, the policy treats every call as
/// unique and never returns a cached value, effectively making the cache a no-op.
/// </para>
///
/// <para>
/// This handler must sit **outside** the cache policy in the handler chain so the
/// <c>Context</c> exists by the time the cache policy looks at it. The default cache key
/// here is the request URI; for production code you would normally include method,
/// headers, or auth identity as needed.
/// </para>
/// </summary>
public sealed class PollyCacheKeyHandler : DelegatingHandler
{
    /// <summary>
    /// The property name <c>PolicyHttpMessageHandler</c> uses to stash the Polly
    /// <see cref="Context"/> on <see cref="HttpRequestMessage.Options"/>.
    /// </summary>
    private const string PolicyContextKey = "PolicyExecutionContext";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri is not null)
        {
            var key = $"{request.Method} {request.RequestUri.AbsoluteUri}";
            var optionsKey = new HttpRequestOptionsKey<Context>(PolicyContextKey);

            if (request.Options.TryGetValue(optionsKey, out var existing) && existing is not null)
            {
                if (string.IsNullOrEmpty(existing.OperationKey))
                {
                    request.Options.Set(optionsKey, new Context(key, existing));
                }
            }
            else
            {
                request.Options.Set(optionsKey, new Context(key));
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
