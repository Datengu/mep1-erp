using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Mep1.Erp.TimesheetWeb.Services;

public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _ctx;

    public BearerTokenHandler(IHttpContextAccessor ctx) => _ctx = ctx;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpCtx = _ctx.HttpContext;

        var token =
            httpCtx?.User?.FindFirst("access_token")?.Value;

        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return base.SendAsync(request, cancellationToken);
    }
}
