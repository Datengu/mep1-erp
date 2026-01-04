using System.Net.Http.Json;
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

    public async Task<TimesheetLoginResultDto?> LoginAsync(
    string username, string password)
    {
        var res = await _http.PostAsJsonAsync(
            "api/timesheet/login",
            new LoginTimesheetDto(username, password));

        if (!res.IsSuccessStatusCode)
            return null;

        return await res.Content
            .ReadFromJsonAsync<TimesheetLoginResultDto>();
    }

    private sealed class CreateEntryResponse
    {
        public int Id { get; set; }
    }
}
