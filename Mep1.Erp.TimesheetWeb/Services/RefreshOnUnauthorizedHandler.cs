using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Mep1.Erp.TimesheetWeb.Services;

public sealed class RefreshOnUnauthorizedHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _ctx;
    private readonly IHttpClientFactory _httpFactory;

    public RefreshOnUnauthorizedHandler(IHttpContextAccessor ctx, IHttpClientFactory httpFactory)
    {
        _ctx = ctx;
        _httpFactory = httpFactory;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // buffer body so we can retry
        var cloned = await CloneHttpRequestMessageAsync(request);

        var res = await base.SendAsync(request, cancellationToken);

        if (res.StatusCode != HttpStatusCode.Unauthorized)
            return res;

        var httpCtx = _ctx.HttpContext;
        if (httpCtx?.User?.Identity?.IsAuthenticated != true)
            return res;

        var refreshToken = httpCtx.User.FindFirst("refresh_token")?.Value;
        if (string.IsNullOrWhiteSpace(refreshToken))
            return res;

        // attempt refresh via auth-only client (NO refresh handler in pipeline)
        var authClient = _httpFactory.CreateClient("ErpAuth");

        using var refreshReq = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { RefreshToken = refreshToken })
        };

        using var refreshRes = await authClient.SendAsync(refreshReq, cancellationToken);
        if (!refreshRes.IsSuccessStatusCode)
            return res;

        var refreshed = await refreshRes.Content.ReadFromJsonAsync<RefreshResponse>(cancellationToken: cancellationToken);
        if (refreshed == null)
            return res;

        // update auth cookie claims
        await ReplaceTokensAsync(httpCtx, refreshed.AccessToken, refreshed.RefreshToken);

        // dispose old response and retry once
        res.Dispose();
        return await base.SendAsync(cloned, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage req)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri);

        // copy headers
        foreach (var header in req.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        // copy content (buffer)
        if (req.Content != null)
        {
            var bytes = await req.Content.ReadAsByteArrayAsync();
            var newContent = new ByteArrayContent(bytes);

            foreach (var header in req.Content.Headers)
                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);

            clone.Content = newContent;
        }

        return clone;
    }

    private static async Task ReplaceTokensAsync(HttpContext ctx, string newAccessToken, string newRefreshToken)
    {
        var identity = ctx.User.Identity as ClaimsIdentity;
        if (identity == null) return;

        // remove old token claims
        foreach (var c in identity.FindAll("access_token").ToList())
            identity.RemoveClaim(c);

        foreach (var c in identity.FindAll("refresh_token").ToList())
            identity.RemoveClaim(c);

        identity.AddClaim(new Claim("access_token", newAccessToken));
        identity.AddClaim(new Claim("refresh_token", newRefreshToken));

        var principal = new ClaimsPrincipal(identity);

        var authResult = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        await ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            authResult.Properties ?? new AuthenticationProperties { AllowRefresh = true });
    }

    private sealed record RefreshResponse(
        string AccessToken,
        DateTime ExpiresUtc,
        string RefreshToken,
        DateTime RefreshExpiresUtc
    );
}
