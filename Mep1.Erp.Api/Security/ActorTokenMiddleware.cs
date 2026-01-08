using Microsoft.AspNetCore.Http;

namespace Mep1.Erp.Api.Security;

public sealed class ActorTokenMiddleware
{
    private readonly RequestDelegate _next;

    public ActorTokenMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context, ActorTokenService tokens)
    {
        // Backward compatible: if not present, do nothing.
        if (context.Request.Headers.TryGetValue("X-Actor-Token", out var tokenValues))
        {
            var token = tokenValues.ToString();

            if (tokens.TryValidate(token, out var actor, out _))
            {
                context.Items["Actor"] = actor;
            }
            // If invalid, we also do nothing here.
            // Enforcement happens per-endpoint (RequireAdminActor).
        }

        await _next(context);
    }
}
