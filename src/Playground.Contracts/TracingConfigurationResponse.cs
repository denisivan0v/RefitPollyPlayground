using System.Text.Json.Serialization;

namespace Playground.Contracts;

/// <summary>
/// Mirrors <c>TraceControlPlaneService.Models.TracingConfigurationResponse</c>.
/// Same JSON shape so both server and client can share this type.
/// </summary>
public sealed class TracingConfigurationResponse
{
    [JsonPropertyName("amwId")]
    public string AmwId { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("enableTracing")]
    public bool EnableTracing { get; init; }

    [JsonPropertyName("storageResourceId")]
    public string StorageResourceId { get; init; } = string.Empty;

    [JsonPropertyName("azureStorageBlobEndpoint")]
    public string AzureStorageBlobEndpoint { get; init; } = string.Empty;

    [JsonPropertyName("traceDurationWindowInSeconds")]
    public int TraceDurationWindowInSeconds { get; init; }

    [JsonPropertyName("lastChangedTimeStamp")]
    public DateTime LastChangedTimeStamp { get; init; }

    [JsonPropertyName("isDeleted")]
    public bool IsDeleted { get; init; }
}
