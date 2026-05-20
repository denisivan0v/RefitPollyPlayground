namespace Playground.Contracts;

/// <summary>
/// Collection envelope — same <c>value</c> array convention as the real service.
/// </summary>
public sealed class TracingConfigurationListResponse
{
    public IReadOnlyList<TracingConfigurationResponse> Value { get; init; }
        = Array.Empty<TracingConfigurationResponse>();
}
