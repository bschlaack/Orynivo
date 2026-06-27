using Microsoft.AspNetCore.Http;

namespace Orynivo.Server.Middleware;

/// <summary>
/// Middleware that enforces API key authentication for all endpoints except
/// <c>/api/health</c>. The key may be supplied via the <c>X-Api-Key</c> request
/// header or the <c>key</c> query-string parameter (query-string allows direct
/// use in FFmpeg and browser URLs).
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, ServerSettings settings)
{
    private static readonly PathString HealthPath = new("/api/health");

    /// <summary>
    /// Checks the request for a valid API key before passing it to the next middleware.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(HealthPath))
        {
            await next(context);
            return;
        }

        var provided = context.Request.Headers["X-Api-Key"].FirstOrDefault()
            ?? context.Request.Query["key"].FirstOrDefault();

        if (string.IsNullOrEmpty(settings.ApiKey)
            || !string.Equals(provided, settings.ApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"error":"Unauthorized"}""");
            return;
        }

        await next(context);
    }
}
