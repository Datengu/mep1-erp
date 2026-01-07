using Mep1.Erp.Core;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Mep1.Erp.Desktop
{
    public class ErpApiClient
    {
        private readonly HttpClient _http;

        public ErpApiClient(string baseUrl, string? apiKey = null)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(30)
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
                _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(int daysAhead)
        {
            var url = $"api/dashboard/summary?daysAhead={daysAhead}";
            var result = await _http.GetFromJsonAsync<DashboardSummaryDto>(url);

            if (result == null)
                throw new InvalidOperationException("Dashboard summary response was empty.");

            return result;
        }

        public async Task<List<DueScheduleEntryDto>> GetDueScheduleAsync()
        {
            var url = "api/dashboard/due-schedule";
            var result = await _http.GetFromJsonAsync<List<DueScheduleEntryDto>>(url);
            return result ?? new List<DueScheduleEntryDto>();
        }

        public async Task<List<UpcomingApplicationEntryDto>> GetUpcomingApplicationsAsync(int daysAhead)
        {
            var url = $"api/dashboard/upcoming-applications?daysAhead={daysAhead}";
            var result = await _http.GetFromJsonAsync<List<UpcomingApplicationEntryDto>>(url);
            return result ?? new List<UpcomingApplicationEntryDto>();
        }

        public async Task<List<InvoiceListEntryDto>> GetInvoicesAsync()
        {
            var result = await _http.GetFromJsonAsync<List<InvoiceListEntryDto>>("api/invoices");
            return result ?? new List<InvoiceListEntryDto>();
        }

        public async Task<List<PeopleSummaryRowDto>> GetPeopleSummaryAsync()
        {
            var result = await _http.GetFromJsonAsync<List<PeopleSummaryRowDto>>("api/people/summary");
            return result ?? new List<PeopleSummaryRowDto>();
        }

        public async Task<PersonDrilldownDto> GetPersonDrilldownAsync(int workerId)
        {
            var result = await _http.GetFromJsonAsync<PersonDrilldownDto>(
                $"api/people/{workerId}/drilldown");

            if (result == null)
                throw new InvalidOperationException("Person drilldown response was empty.");

            return result;
        }

        public async Task<PortalAccessDto> GetPortalAccessAsync(int workerId)
        {
            var result = await _http.GetFromJsonAsync<PortalAccessDto>($"api/people/{workerId}/portal-access");
            if (result == null) throw new InvalidOperationException("Portal access response was empty.");
            return result;
        }

        public async Task<CreatePortalAccessResult> CreatePortalAccessAsync(int workerId, CreatePortalAccessRequest dto)
        {
            var resp = await _http.PostAsJsonAsync($"api/people/{workerId}/portal-access", dto);
            resp.EnsureSuccessStatusCode();

            var created = await resp.Content.ReadFromJsonAsync<CreatePortalAccessResult>();
            if (created == null) throw new InvalidOperationException("Create portal access response was empty.");
            return created;
        }

        public async Task UpdatePortalAccessAsync(int workerId, UpdatePortalAccessRequest dto)
        {
            var req = new HttpRequestMessage(HttpMethod.Patch, $"api/people/{workerId}/portal-access")
            {
                Content = JsonContent.Create(dto)
            };

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode(); // expects 204
        }

        public async Task<ResetPortalPasswordResult> ResetPortalPasswordAsync(int workerId)
        {
            var resp = await _http.PostAsync($"api/people/{workerId}/portal-access/reset-password", content: null);
            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<ResetPortalPasswordResult>();
            if (result == null) throw new InvalidOperationException("Reset password response was empty.");
            return result;
        }

        public async Task<List<ProjectSummaryDto>> GetProjectSummariesAsync()
        {
            var result = await _http.GetFromJsonAsync<List<ProjectSummaryDto>>("api/projects/summary");
            return result ?? new List<ProjectSummaryDto>();
        }

        public async Task<ProjectDrilldownDto> GetProjectDrilldownAsync(string jobKey, int recentTake = 25)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}/drilldown?recentTake={recentTake}";
            var result = await _http.GetFromJsonAsync<ProjectDrilldownDto>(url);

            if (result == null)
                throw new InvalidOperationException("Project drilldown response was empty.");

            return result;
        }

        public async Task SetProjectActiveAsync(string jobKey, bool isActive)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}/active";

            var request = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = JsonContent.Create(new { isActive })
            };

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode(); // expects 204 NoContent
        }

        public async Task<SupplierCostRowDto> AddProjectSupplierCostAsync(string jobKey, UpsertSupplierCostDto dto)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}/supplier-costs";
            var resp = await _http.PostAsJsonAsync(url, dto);
            resp.EnsureSuccessStatusCode();

            var created = await resp.Content.ReadFromJsonAsync<SupplierCostRowDto>();
            if (created == null)
                throw new InvalidOperationException("Add supplier cost response was empty.");

            return created;
        }

        public async Task UpdateProjectSupplierCostAsync(string jobKey, int id, UpsertSupplierCostDto dto)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}/supplier-costs/{id}";
            var resp = await _http.PutAsJsonAsync(url, dto);
            resp.EnsureSuccessStatusCode(); // expects 204 NoContent from your controller
        }

        public async Task DeleteProjectSupplierCostAsync(string jobKey, int id)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}/supplier-costs/{id}";
            var resp = await _http.DeleteAsync(url);
            resp.EnsureSuccessStatusCode(); // expects 204 NoContent from your controller
        }

        public async Task<List<SupplierDto>> GetSuppliersAsync(bool includeInactive = false)
        {
            var url = $"api/suppliers?includeInactive={includeInactive.ToString().ToLowerInvariant()}";
            var result = await _http.GetFromJsonAsync<List<SupplierDto>>(url);
            return result ?? new List<SupplierDto>();
        }

        public async Task<SupplierDto> AddSupplierAsync(UpsertSupplierDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/suppliers", dto);
            resp.EnsureSuccessStatusCode();

            var created = await resp.Content.ReadFromJsonAsync<SupplierDto>();
            if (created == null) throw new InvalidOperationException("Add supplier response was empty.");
            return created;
        }

        public async Task UpdateSupplierAsync(int id, UpsertSupplierDto dto)
        {
            var resp = await _http.PutAsJsonAsync($"api/suppliers/{id}", dto);
            resp.EnsureSuccessStatusCode();
        }

        public async Task DeactivateSupplierAsync(int id)
        {
            var resp = await _http.PostAsync($"api/suppliers/{id}/deactivate", content: null);
            resp.EnsureSuccessStatusCode();
        }

    }
}
