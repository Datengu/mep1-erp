using System.Net.Http.Json;
using Mep1.Erp.TimesheetWeb.Models;
using Mep1.Erp.Core;

namespace Mep1.Erp.TimesheetWeb.Services;

public sealed class ErpTimesheetApiClient
{
    private readonly HttpClient _http;

    public ErpTimesheetApiClient(HttpClient http)
    {
        _http = http;
    }

    public Task<List<TimesheetProjectOptionDto>?> GetActiveProjectsAsync()
        => _http.GetFromJsonAsync<List<TimesheetProjectOptionDto>>("api/timesheet/projects");

    public async Task<int> CreateEntryAsync(CreateTimesheetEntryDto dto)
    {
        var res = await _http.PostAsJsonAsync("api/timesheet/entries", dto);
        res.EnsureSuccessStatusCode();

        var payload = await res.Content.ReadFromJsonAsync<CreateEntryResponse>();
        return payload?.Id ?? 0;
    }

    public async Task<TimesheetLoginResponse?> LoginAsync(string username, string password)
    {
        var req = new TimesheetLoginRequest
        {
            Username = username,
            Password = password
        };

        var resp = await _http.PostAsJsonAsync("api/timesheet/login", req);
        if (!resp.IsSuccessStatusCode)
            return null;

        return await resp.Content.ReadFromJsonAsync<TimesheetLoginResponse>();
    }


    private sealed class CreateEntryResponse
    {
        public int Id { get; set; }
    }
}
