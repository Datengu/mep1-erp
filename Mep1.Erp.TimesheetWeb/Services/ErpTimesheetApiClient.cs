using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net;
using Mep1.Erp.Core.Contracts;

namespace Mep1.Erp.TimesheetWeb.Services;

public sealed class ErpTimesheetApiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private const string CorrelationHeader = "X-Correlation-Id";

    public abstract class TimesheetApiException : Exception
    {
        protected TimesheetApiException(string message, HttpStatusCode? statusCode, string? correlationId, string? responseBody = null)
            : base(message)
        {
            StatusCode = statusCode;
            CorrelationId = correlationId;
            ResponseBody = responseBody;
        }

        public HttpStatusCode? StatusCode { get; }
        public string? CorrelationId { get; }
        public string? ResponseBody { get; }
    }

    public sealed class TimesheetApiAuthException : TimesheetApiException
    {
        public TimesheetApiAuthException(string message, HttpStatusCode statusCode, string? correlationId, string? responseBody = null)
            : base(message, statusCode, correlationId, responseBody) { }
    }

    public sealed class TimesheetApiValidationException : TimesheetApiException
    {
        public TimesheetApiValidationException(string message, string? correlationId, string? responseBody = null)
            : base(message, HttpStatusCode.BadRequest, correlationId, responseBody) { }
    }

    public sealed class TimesheetApiNotFoundException : TimesheetApiException
    {
        public TimesheetApiNotFoundException(string message, string? correlationId, string? responseBody = null)
            : base(message, HttpStatusCode.NotFound, correlationId, responseBody) { }
    }

    public sealed class TimesheetApiRateLimitException : TimesheetApiException
    {
        public TimesheetApiRateLimitException(string message, string? correlationId, string? responseBody = null)
            : base(message, HttpStatusCode.TooManyRequests, correlationId, responseBody) { }
    }

    public sealed class TimesheetApiServerException : TimesheetApiException
    {
        public TimesheetApiServerException(string message, HttpStatusCode statusCode, string? correlationId, string? responseBody = null)
            : base(message, statusCode, correlationId, responseBody) { }
    }

    public sealed class TimesheetApiUnavailableException : TimesheetApiException
    {
        public TimesheetApiUnavailableException(string message, Exception? inner = null)
            : base(message, null, null)
        {
            if (inner != null) InnerExceptionCaptured = inner;
        }

        // purely to keep the original exception accessible without changing base Exception internals
        public Exception? InnerExceptionCaptured { get; }
    }

    private static string? TryGetCorrelationId(HttpResponseMessage res)
    {
        if (res.Headers.TryGetValues(CorrelationHeader, out var vals))
            return vals.FirstOrDefault();
        return null;
    }

    private static async Task<string?> TryReadBodyAsync(HttpResponseMessage res)
    {
        try
        {
            return await res.Content.ReadAsStringAsync();
        }
        catch
        {
            return null;
        }
    }

    private static void ThrowForNonSuccess(HttpResponseMessage res, string? correlationId, string? body)
    {
        var sc = res.StatusCode;

        // safe, user-facing message defaults
        string msg = sc switch
        {
            HttpStatusCode.BadRequest => "Some fields were invalid. Please review and try again.",
            HttpStatusCode.Unauthorized => "You are no longer signed in. Please log in again.",
            HttpStatusCode.Forbidden => "You do not have permission to do that.",
            HttpStatusCode.NotFound => "That item could not be found (it may have been deleted).",
            HttpStatusCode.TooManyRequests => "Too many attempts. Please wait and try again.",
            _ when (int)sc >= 500 => "The server ran into a problem. Please try again.",
            _ => $"Request failed (HTTP {(int)sc})."
        };

        if (sc is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TimesheetApiAuthException(msg, sc, correlationId, body);

        if (sc == HttpStatusCode.BadRequest)
            throw new TimesheetApiValidationException(msg, correlationId, body);

        if (sc == HttpStatusCode.NotFound)
            throw new TimesheetApiNotFoundException(msg, correlationId, body);

        if (sc == HttpStatusCode.TooManyRequests)
            throw new TimesheetApiRateLimitException(msg, correlationId, body);

        if ((int)sc >= 500)
            throw new TimesheetApiServerException(msg, sc, correlationId, body);

        throw new TimesheetApiServerException(msg, sc, correlationId, body);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req)
    {
        AddApiKeyHeader(req);

        try
        {
            return await _http.SendAsync(req);
        }
        catch (TaskCanceledException ex)
        {
            throw new TimesheetApiUnavailableException("API request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TimesheetApiUnavailableException("Cannot reach the API server.", ex);
        }
    }

    private async Task<T?> SendJsonAsync<T>(HttpRequestMessage req)
    {
        using var res = await SendAsync(req);

        if (res.IsSuccessStatusCode)
            return await res.Content.ReadFromJsonAsync<T>(_jsonOptions);

        var correlationId = TryGetCorrelationId(res);
        var body = await TryReadBodyAsync(res);
        ThrowForNonSuccess(res, correlationId, body);
        return default;
    }

    private async Task SendNoContentAsync(HttpRequestMessage req)
    {
        using var res = await SendAsync(req);

        if (res.IsSuccessStatusCode)
            return;

        var correlationId = TryGetCorrelationId(res);
        var body = await TryReadBodyAsync(res);
        ThrowForNonSuccess(res, correlationId, body);
    }

    public ErpTimesheetApiClient(HttpClient http, IConfiguration config)
    {
        _http = http;

        // base address should already be set in Program.cs AddHttpClient
        _apiKey = config["ErpApi:ApiKey"] ?? "";
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Missing ErpApi:ApiKey in configuration.");
    }

    private void AddApiKeyHeader(HttpRequestMessage req)
    {
        // match your API key middleware header name
        req.Headers.Remove("X-Api-Key");
        req.Headers.Add("X-Api-Key", _apiKey);
    }

    public async Task<List<TimesheetProjectOptionDto>?> GetActiveProjectsAsync()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/timesheet/projects");
        return await SendJsonAsync<List<TimesheetProjectOptionDto>>(req);
    }

    public sealed record TimesheetLoginRequest(string Username, string Password, bool RememberMe);

    public sealed record TimesheetLoginResponse(
        int WorkerId,
        string Username,
        string Role,
        string Name,
        string Initials,
        bool MustChangePassword,
        string AccessToken,
        DateTime ExpiresUtc,
        string? RefreshToken,
        DateTime? RefreshExpiresUtc
    );

    public sealed record TimesheetLogoutRequest(string RefreshToken);

    public sealed record TimesheetRefreshRequest(string RefreshToken);

    public sealed record TimesheetRefreshResponse(
        string AccessToken,
        DateTime ExpiresUtc,
        string RefreshToken,
        DateTime RefreshExpiresUtc
    );

    public async Task<TimesheetLoginResponse?> LoginAsync(string username, string password, bool rememberMe)
    {
        var body = new TimesheetLoginRequest(username, password, rememberMe);

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(body, options: _jsonOptions)
        };

        try
        {
            return await SendJsonAsync<TimesheetLoginResponse>(req);
        }
        catch (TimesheetApiAuthException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }
    }

    public async Task CreateTimesheetEntryAsync(CreateTimesheetEntryDto dto)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/timesheet/entries")
        {
            Content = JsonContent.Create(dto, options: _jsonOptions)
        };

        await SendNoContentAsync(req);
    }

    public async Task<TimesheetEntryEditDto?> GetTimesheetEntryAsync(int id)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/timesheet/entries/{id}");

        try
        {
            return await SendJsonAsync<TimesheetEntryEditDto>(req);
        }
        catch (TimesheetApiNotFoundException)
        {
            return null;
        }
    }

    public async Task<List<TimesheetEntrySummaryDto>?> GetTimesheetEntriesAsync(int skip = 0, int take = 100)
    {
        var url = $"/api/timesheet/entries?skip={skip}&take={take}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendJsonAsync<List<TimesheetEntrySummaryDto>>(req);
    }

    public async Task UpdateTimesheetEntryAsync(int id, UpdateTimesheetEntryDto dto)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/timesheet/entries/{id}")
        {
            Content = JsonContent.Create(dto, options: _jsonOptions)
        };

        await SendNoContentAsync(req);
    }

    public async Task DeleteTimesheetEntryAsync(int id)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/timesheet/entries/{id}");
        await SendNoContentAsync(req);
    }

    public async Task<WorkerSignatureDto?> GetWorkerSignatureAsync(int workerId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/timesheet/workers/{workerId}/signature");

        try
        {
            return await SendJsonAsync<WorkerSignatureDto>(req);
        }
        catch (TimesheetApiNotFoundException)
        {
            return null;
        }
    }

    public async Task SetWorkerSignatureAsync(int workerId, string signatureName)
    {
        var dto = new UpdateWorkerSignatureDto { SignatureName = signatureName };

        using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/timesheet/workers/{workerId}/signature")
        {
            Content = JsonContent.Create(dto, options: _jsonOptions)
        };

        await SendNoContentAsync(req);
    }

    public async Task<WorkerSignatureDto?> GetOwnerSignatureAsync()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/timesheet/owner-signature");

        try
        {
            return await SendJsonAsync<WorkerSignatureDto>(req);
        }
        catch (TimesheetApiNotFoundException)
        {
            return null;
        }
    }

    public sealed record ChangePasswordRequest(string Username, string CurrentPassword, string NewPassword);

    public async Task<(bool ok, string? error)> ChangePasswordAsync(string username, string currentPassword, string newPassword)
    {
        var body = new ChangePasswordRequest(username, currentPassword, newPassword);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(body, options: _jsonOptions)
        };

        using var res = await SendAsync(req);

        if (res.IsSuccessStatusCode)
            return (true, null);

        var msg = await TryReadBodyAsync(res);
        if (string.IsNullOrWhiteSpace(msg))
            msg = $"HTTP {(int)res.StatusCode} {res.StatusCode}";

        return (false, msg);
    }

    public async Task<TimesheetRefreshResponse?> RefreshAsync(string refreshToken)
    {
        var body = new TimesheetRefreshRequest(refreshToken);

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh")
        {
            Content = JsonContent.Create(body, options: _jsonOptions)
        };

        try
        {
            return await SendJsonAsync<TimesheetRefreshResponse>(req);
        }
        catch (TimesheetApiAuthException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }
    }

    public async Task LogoutAsync(string refreshToken)
    {
        var body = new TimesheetLogoutRequest(refreshToken);

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout")
        {
            Content = JsonContent.Create(body, options: _jsonOptions)
        };

        try
        {
            using var res = await SendAsync(req);
            // ignore non-success (by design)
        }
        catch (TimesheetApiUnavailableException)
        {
            // ignore (by design)
        }
    }

    public async Task<List<TimesheetCodeDto>> GetTimesheetCodesAsync()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/timesheet/codes");
        return await SendJsonAsync<List<TimesheetCodeDto>>(req) ?? new();
    }

    public async Task<List<string>?> GetCcfRefsForJobAsync(string jobKey)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/timesheet/ccf-refs?jobKey={Uri.EscapeDataString(jobKey)}");

        return await SendJsonAsync<List<string>>(req);
    }
}
