using System.Net.Http;
using Microsoft.AspNetCore.Http;

namespace Mep1.Erp.TimesheetWeb.Infrastructure;

public sealed class CorrelationIdHandler : DelegatingHandler
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly IHttpContextAccessor _http;

    public CorrelationIdHandler(IHttpContextAccessor http)
    {
        _http = http;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var ctx = _http.HttpContext;
        if (ctx != null)
        {
            request.Headers.Remove(HeaderName);
            request.Headers.Add(HeaderName, ctx.TraceIdentifier);
        }

        return base.SendAsync(request, cancellationToken);
    }
}