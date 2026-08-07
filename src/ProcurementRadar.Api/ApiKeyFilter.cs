using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ProcurementRadar.Api;

/// <summary>
/// One token for one team (D-16). Not a login.
/// </summary>
/// <remarks>
/// A filter rather than middleware, so it is part of the pipeline the OpenAPI
/// document describes and Swagger can offer an Authorize box for it. What it
/// defends against is modest and worth being honest about: another device on the
/// same home network changing a setting by accident. It is also the only thing
/// between this api and anything that can reach port 5080.
/// </remarks>
public sealed class ApiKeyFilter : IAsyncActionFilter
{
    public const string HeaderName = "X-Api-Token";

    private readonly string? _token;

    public ApiKeyFilter(IConfiguration configuration)
    {
        _token = configuration["PR_API_TOKEN"];
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // No token configured means no gate. Refusing to start would leave the
        // api unreachable with the reason invisible; the warning at startup is
        // where that gets said instead.
        if (!string.IsNullOrWhiteSpace(_token)
            && !string.Equals(context.HttpContext.Request.Headers[HeaderName], _token, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedResult();

            return;
        }

        await next().ConfigureAwait(false);
    }
}
