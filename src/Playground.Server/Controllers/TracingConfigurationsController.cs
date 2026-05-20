using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Playground.Contracts;

namespace Playground.Server.Controllers;

/// <summary>
/// Mirrors <c>TracingConfigurationsController</c> in the real repo:
///   GET /tracing-configurations?namespace={ns}&amp;endpoint={endpoint}
///   GET /tracing-configurations?appInsightsResourceId={id}
/// Returns 400 for bad input, 200 with empty list when nothing matches.
/// </summary>
[ApiController]
[Route("tracing-configurations")]
public sealed partial class TracingConfigurationsController : ControllerBase
{
    [GeneratedRegex(
        @"^/subscriptions/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/resourcegroups/[^/]+/providers/microsoft\.insights/components/[^/]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AppInsightsResourceIdPattern();

    private readonly InMemoryTracingConfigurationStore _store;
    private readonly ILogger<TracingConfigurationsController> _logger;

    public TracingConfigurationsController(
        InMemoryTracingConfigurationStore store,
        ILogger<TracingConfigurationsController> logger)
    {
        _store = store;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetByQuery(
        [FromQuery] string? endpoint,
        [FromQuery] string? @namespace,
        [FromQuery] string? appInsightsResourceId)
    {
        if (@namespace is not null)
        {
            if (string.IsNullOrWhiteSpace(@namespace))
                return Problem(400, "BadRequest", "'namespace' query parameter is empty.");

            if (endpoint is not null && string.IsNullOrWhiteSpace(endpoint))
                return Problem(400, "BadRequest", "'endpoint' query parameter is empty when provided.");

            _logger.LogInformation(
                "Query by Geneva log account: endpoint='{Endpoint}' namespace='{Namespace}'",
                endpoint ?? "*", @namespace);

            return Ok(_store.GetByGenevaLogAccount(endpoint, @namespace));
        }

        if (appInsightsResourceId is not null)
        {
            if (string.IsNullOrWhiteSpace(appInsightsResourceId))
                return Problem(400, "BadRequest", "'appInsightsResourceId' query parameter is empty.");

            if (!AppInsightsResourceIdPattern().IsMatch(appInsightsResourceId))
                return Problem(400, "BadRequest",
                    "'appInsightsResourceId' must be a valid Application Insights ARM resource ID.");

            _logger.LogInformation(
                "Query by appInsightsResourceId='{AppInsightsResourceId}'", appInsightsResourceId);

            return Ok(_store.GetByAppInsightsResourceId(appInsightsResourceId));
        }

        return Problem(400, "BadRequest",
            "At least one query parameter is required: 'namespace' (with optional 'endpoint') or 'appInsightsResourceId'.");
    }

    private IActionResult Problem(int statusCode, string code, string message)
    {
        Response.Headers["x-ms-error-code"] = code;
        return StatusCode(statusCode, new
        {
            error = new { code, message }
        });
    }
}
