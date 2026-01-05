using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mep1.Erp.Core;

namespace Mep1.Erp.TimesheetWeb.Services;

public sealed class ErpTimesheetApiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

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
        AddApiKeyHeader(req);

        using var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();

        return await res.Content.ReadFromJsonAsync<List<TimesheetProjectOptionDto>>();
    }

    public sealed record TimesheetLoginRequest(string Username, string Password);

    public sealed record TimesheetLoginResponse(int WorkerId, string Name, string Initials);

    public async Task<TimesheetLoginResponse?> LoginAsync(string username, string password)
    {
        var body = new TimesheetLoginRequest(username, password);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/timesheet/login")
        {
            Content = JsonContent.Create(body)
        };
        AddApiKeyHeader(req);

        using var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();

        return await res.Content.ReadFromJsonAsync<TimesheetLoginResponse>();
    }

    public async Task CreateTimesheetEntryAsync(CreateTimesheetEntryDto dto)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/timesheet/entries")
        {
            Content = JsonContent.Create(dto)
        };
        AddApiKeyHeader(req);

        using var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }

    public async Task<List<TimesheetEntrySummaryDto>?> GetTimesheetEntriesAsync(
    int workerId,
    int skip = 0,
    int take = 100)
    {
        var url = $"/api/timesheet/entries?workerId={workerId}&skip={skip}&take={take}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddApiKeyHeader(req);

        using var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();

        return await res.Content.ReadFromJsonAsync<List<TimesheetEntrySummaryDto>>();
    }

    public async Task UpdateTimesheetEntryAsync(int id, UpdateTimesheetEntryDto dto)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/timesheet/entries/{id}")
        {
            Content = JsonContent.Create(dto)
        };
        AddApiKeyHeader(req);

        using var res = await _http.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }

}
