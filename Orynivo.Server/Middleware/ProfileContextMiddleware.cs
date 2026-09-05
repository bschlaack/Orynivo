using Orynivo.Library;

namespace Orynivo.Server.Middleware;

/// <summary>Applies the authenticated request's profile to personal library operations.</summary>
public sealed class ProfileContextMiddleware(RequestDelegate next, ServerSettings settings)
{
    /// <summary>Reads <c>X-Orynivo-Profile</c> and scopes the async database context.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var requested = context.Request.Headers["X-Orynivo-Profile"].FirstOrDefault()
            ?? context.Request.Query["profile"].FirstOrDefault();
        var profileId = string.IsNullOrWhiteSpace(requested) ? "standard" : requested.Trim();
        var profile = settings.Profiles.FirstOrDefault(p =>
            string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Unknown profile." });
            return;
        }

        var previous = AudioDatabase.ActiveProfileId;
        AudioDatabase.SetActiveProfile(profile.Id);
        try
        {
            await next(context);
        }
        finally
        {
            AudioDatabase.SetActiveProfile(previous);
        }
    }
}
