using DocumentFormat.OpenXml.Wordprocessing;
using Mep1.Erp.Core.Contracts;
using Microsoft.Win32;
using System.Linq;
using System.IO;
using System.Xml.Linq;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace Mep1.Erp.Desktop
{
    public class ErpApiClient
    {
        private readonly HttpClient _http;
        public string BaseUrl { get; }
        public bool IsStaging => (BaseUrl ?? "").IndexOf("staging", StringComparison.OrdinalIgnoreCase) >= 0;

        private static readonly Lazy<string> _desktopVersion = new Lazy<string>(ComputeDesktopVersionString);


        private sealed class TimingHandler : DelegatingHandler
        {
            private static long _seq;

            public TimingHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var id = Interlocked.Increment(ref _seq);
                var sw = Stopwatch.StartNew();

                Debug.WriteLine($"[HTTP {id} ▶] {request.Method} {request.RequestUri}");

                System.Diagnostics.Debug.WriteLine("BaseDirectory = " + AppContext.BaseDirectory);

                try
                {
                    var resp = await base.SendAsync(request, cancellationToken);
                    sw.Stop();

                    Debug.WriteLine($"[HTTP {id} ◀] {(int)resp.StatusCode} {resp.ReasonPhrase} {sw.ElapsedMilliseconds} ms  ({request.Method} {request.RequestUri})");

                    // If server says "Upgrade Required", throw a friendly exception.
                    // This will propagate through GetFromJsonAsync/PostAsJsonAsync/etc automatically.
                    if ((int)resp.StatusCode == 426)
                    {
                        var body = await resp.Content.ReadAsStringAsync();
                        throw new InvalidOperationException(
                            "An update is required. Please close and re-open the desktop app to update.\n\n" +
                            body);
                    }

                    return resp;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Debug.WriteLine($"[HTTP {id} ✖] {sw.ElapsedMilliseconds} ms  ({request.Method} {request.RequestUri})  {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            }
        }

        public ErpApiClient(string baseUrl)
        {
            BaseUrl = (baseUrl ?? "").TrimEnd('/') + "/";

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            _http = new HttpClient(new TimingHandler(handler))
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };

            SetClientHeaders();

            // Ask server for compressed responses (helps big JSON like summaries)
            _http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            _http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
        }

        public async Task<DashboardSummaryDto> GetDashboardSummaryAsync(int daysAhead)
        {
            var url = $"api/dashboard/summary?daysAhead={daysAhead}";

            var resp = await _http.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Dashboard summary failed ({(int)resp.StatusCode}): {body}");

            var dto = System.Text.Json.JsonSerializer.Deserialize<DashboardSummaryDto>(
                body,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (dto == null)
                throw new InvalidOperationException("Dashboard summary response was empty.");

            return dto;
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

        public async Task SetWorkerActiveAsync(int workerId, bool isActive)
        {
            var url = $"api/people/{workerId}/active";

            var request = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = JsonContent.Create(new { isActive })
            };

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode(); // expects 204 NoContent
        }

        public async Task<PortalAccessDto> GetPortalAccessAsync(int workerId)
        {
            var result = await _http.GetFromJsonAsync<PortalAccessDto>($"api/people/{workerId}/portal-access");
            if (result == null) throw new InvalidOperationException("Portal access response was empty.");
            return result;
        }

        public async Task<CreatePortalAccessResultDto> CreatePortalAccessAsync(int workerId, CreatePortalAccessRequestDto dto)
        {
            var resp = await _http.PostAsJsonAsync($"api/people/{workerId}/portal-access", dto);
            resp.EnsureSuccessStatusCode();

            var created = await resp.Content.ReadFromJsonAsync<CreatePortalAccessResultDto>();
            if (created == null) throw new InvalidOperationException("Create portal access response was empty.");
            return created;
        }

        public async Task UpdatePortalAccessAsync(int workerId, UpdatePortalAccessRequestDto dto)
        {
            var req = new HttpRequestMessage(HttpMethod.Patch, $"api/people/{workerId}/portal-access")
            {
                Content = JsonContent.Create(dto)
            };

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode(); // expects 204
        }

        public async Task<ResetPortalPasswordResultDto> ResetPortalPasswordAsync(int workerId)
        {
            var resp = await _http.PostAsync($"api/people/{workerId}/portal-access/reset-password", content: null);
            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<ResetPortalPasswordResultDto>();
            if (result == null) throw new InvalidOperationException("Reset password response was empty.");
            return result;
        }

        public async Task<PortalUsernameAvailableDto> GetPortalUsernameAvailabilityAsync(string username)
        {
            var url = $"api/people/portal-access/username-available?username={Uri.EscapeDataString(username ?? "")}";

            var result = await _http.GetFromJsonAsync<PortalUsernameAvailableDto>(url);
            if (result == null)
                throw new InvalidOperationException("Portal username availability response was empty.");

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

        public async Task<CreateWorkerResponseDto> CreateWorkerAsync(CreateWorkerRequestDto request)
        {
            var resp = await _http.PostAsJsonAsync("api/people", request);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"API failed ({(int)resp.StatusCode}): {body}");
            }

            var dto = await resp.Content.ReadFromJsonAsync<CreateWorkerResponseDto>();
            if (dto == null) throw new Exception("API returned empty response.");
            return dto;
        }

        public void SetActorToken(string? token)
        {
            const string headerName = "X-Actor-Token";

            if (_http.DefaultRequestHeaders.Contains(headerName))
                _http.DefaultRequestHeaders.Remove(headerName);

            if (!string.IsNullOrWhiteSpace(token))
                _http.DefaultRequestHeaders.Add(headerName, token);
        }

        public async Task<DesktopAdminLoginResponseDto> DesktopAdminLoginAsync(string username, string password)
        {
            var resp = await _http.PostAsJsonAsync("api/admin/auth/login",
                new DesktopAdminLoginRequestDto(username, password));

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Login failed ({(int)resp.StatusCode}): {body}");
            }

            var dto = await resp.Content.ReadFromJsonAsync<DesktopAdminLoginResponseDto>();
                if (dto == null) throw new Exception("Login returned empty response.");
            
            return dto;
        }

        public async Task<List<AuditLogRowDto>> GetAuditLogsAsync(
            int take = 200,
            int skip = 0,
            string? search = null,
            string? entityType = null,
            string? entityId = null,
            int? actorWorkerId = null,
            string? action = null)
        {
            var qs = new List<string>
            {
                $"take={take}",
                $"skip={skip}"
            };

            if (!string.IsNullOrWhiteSpace(search))
                qs.Add("search=" + Uri.EscapeDataString(search.Trim()));

            if (!string.IsNullOrWhiteSpace(entityType))
                qs.Add("entityType=" + Uri.EscapeDataString(entityType.Trim()));

            if (!string.IsNullOrWhiteSpace(entityId))
                qs.Add("entityId=" + Uri.EscapeDataString(entityId.Trim()));

            if (actorWorkerId.HasValue)
                qs.Add("actorWorkerId=" + actorWorkerId.Value);

            if (!string.IsNullOrWhiteSpace(action))
                qs.Add("action=" + Uri.EscapeDataString(action.Trim()));

            var url = "api/audit?" + string.Join("&", qs);

            var result = await _http.GetFromJsonAsync<List<AuditLogRowDto>>(url);
            return result ?? new List<AuditLogRowDto>();
        }

        public async Task<WorkerForEditDto> GetWorkerForEditAsync(int workerId)
        {
            var result = await _http.GetFromJsonAsync<WorkerForEditDto>($"api/people/{workerId}/edit");
            if (result == null)
                throw new InvalidOperationException("Worker edit response was empty.");
            return result;
        }

        public async Task UpdateWorkerDetailsAsync(int workerId, UpdateWorkerDetailsRequestDto dto)
        {
            var req = new HttpRequestMessage(HttpMethod.Patch, $"api/people/{workerId}")
            {
                Content = JsonContent.Create(dto)
            };

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode(); // expects 204
        }

        public async Task ChangeCurrentWorkerRateAsync(int workerId, ChangeCurrentRateRequestDto dto)
        {
            var resp = await _http.PostAsJsonAsync($"api/people/{workerId}/rates/change-current", dto);
            resp.EnsureSuccessStatusCode(); // expects 204
        }

        public async Task AddWorkerRateAsync(int workerId, AddWorkerRateRequestDto dto)
        {
            var resp = await _http.PostAsJsonAsync($"api/people/{workerId}/rates", dto);
            resp.EnsureSuccessStatusCode(); // expects 204
        }

        public async Task UpdateWorkerRateAmountAsync(int workerId, int rateId, UpdateWorkerRateAmountRequestDto dto)
        {
            var req = new HttpRequestMessage(HttpMethod.Patch, $"api/people/{workerId}/rates/{rateId}")
            {
                Content = JsonContent.Create(dto)
            };

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode(); // expects 204
        }

        public async Task DeleteWorkerRateAsync(int workerId, int rateId)
        {
            var resp = await _http.DeleteAsync($"api/people/{workerId}/rates/{rateId}");
            resp.EnsureSuccessStatusCode(); // expects 204
        }

        public async Task<CreateProjectResponseDto> CreateProjectAsync(CreateProjectRequestDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/projects", dto);

            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var msg = await resp.Content.ReadAsStringAsync();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg)
                    ? "A project with that job name/number already exists."
                    : msg);
            }

            resp.EnsureSuccessStatusCode();

            var created = await resp.Content.ReadFromJsonAsync<CreateProjectResponseDto>();
            if (created == null)
                throw new InvalidOperationException("Create project response was empty.");

            return created;
        }

        public async Task<List<CompanyListItemDto>> GetCompaniesAsync()
        {
            var result = await _http.GetFromJsonAsync<List<CompanyListItemDto>>("api/companies");
            return result ?? new List<CompanyListItemDto>();
        }

        public async Task<List<InvoiceProjectPicklistItemDto>> GetInvoiceProjectPicklistAsync()
        {
            var result = await _http.GetFromJsonAsync<List<InvoiceProjectPicklistItemDto>>("api/projects/picklist/invoices");
            return result ?? new List<InvoiceProjectPicklistItemDto>();
        }

        public async Task<CreateInvoiceResponseDto> CreateInvoiceAsync(CreateInvoiceRequestDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/invoices", dto);

            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var msg = await resp.Content.ReadAsStringAsync();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg)
                    ? "An invoice with that number already exists."
                    : msg);
            }

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"API failed ({(int)resp.StatusCode}): {body}");
            }

            var created = await resp.Content.ReadFromJsonAsync<CreateInvoiceResponseDto>();
            if (created == null) throw new InvalidOperationException("Create invoice response was empty.");

            return created;
        }

        public async Task<InvoiceDetailsDto> GetInvoiceByIdAsync(int id)
        {
            var resp = await _http.GetAsync($"api/invoices/{id}");
            resp.EnsureSuccessStatusCode();

            var dto = await resp.Content.ReadFromJsonAsync<InvoiceDetailsDto>();
            if (dto == null) throw new Exception("Failed to deserialize InvoiceDetailsDto.");

            return dto;
        }

        public async Task<UpdateInvoiceResponseDto> UpdateInvoiceAsync(int id, UpdateInvoiceRequestDto dto)
        {
            var resp = await _http.PutAsJsonAsync($"api/invoices/{id}", dto);
            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<UpdateInvoiceResponseDto>();
            if (result == null) throw new Exception("Failed to deserialize UpdateInvoiceResponseDto.");

            return result;
        }

        public async Task<AuthLoginResponseDto> AuthLoginAsync(string username, string password)
        {
            var resp = await _http.PostAsJsonAsync("api/auth/login",
                new AuthLoginRequestDto(username, password));

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Login failed ({(int)resp.StatusCode}): {body}");
            }

            var dto = await resp.Content.ReadFromJsonAsync<AuthLoginResponseDto>();
            if (dto == null) throw new Exception("Login returned empty response.");

            // Ensure all subsequent calls are JWT-authenticated
            SetBearerToken(dto.AccessToken);

            return dto;
        }

        public void SetBearerToken(string? jwt)
        {
            _http.DefaultRequestHeaders.Authorization = null;

            if (!string.IsNullOrWhiteSpace(jwt))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        }

        public async Task<List<TimesheetProjectOptionDto>> GetTimesheetActiveProjectsAsync()
        {
            var result = await _http.GetFromJsonAsync<List<TimesheetProjectOptionDto>>("api/timesheet/projects");
            return result ?? new List<TimesheetProjectOptionDto>();
        }

        public async Task<List<TimesheetEntrySummaryDto>> GetTimesheetEntriesAsync(
            int skip = 0,
            int take = 50,
            int? subjectWorkerId = null)
        {
            var qs = new List<string>
            {
                $"skip={skip}",
                $"take={take}"
            };

            if (subjectWorkerId.HasValue && subjectWorkerId.Value > 0)
                qs.Add("subjectWorkerId=" + subjectWorkerId.Value);

            var url = "api/timesheet/entries?" + string.Join("&", qs);

            var result = await _http.GetFromJsonAsync<List<TimesheetEntrySummaryDto>>(url);
            return result ?? new List<TimesheetEntrySummaryDto>();
        }

        public async Task CreateTimesheetEntryAsync(CreateTimesheetEntryDto dto, int? subjectWorkerId = null)
        {
            var url = "api/timesheet/entries";

            if (subjectWorkerId.HasValue && subjectWorkerId.Value > 0)
                url += "?subjectWorkerId=" + subjectWorkerId.Value;

            var resp = await _http.PostAsJsonAsync(url, dto);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Create timesheet entry failed ({(int)resp.StatusCode}): {body}");
            }
        }

        public async Task<List<TimesheetCodeDto>> GetTimesheetCodesAsync()
        {
            var resp = await _http.GetAsync("api/timesheet/codes");

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Get timesheet codes failed ({(int)resp.StatusCode}): {body}");
            }

            var result = await resp.Content.ReadFromJsonAsync<List<TimesheetCodeDto>>();
            return result ?? new List<TimesheetCodeDto>();
        }

        public async Task<TimesheetEntryEditDto> GetTimesheetEntryForEditAsync(int id, int? subjectWorkerId = null)
        {
            var url = $"api/timesheet/entries/{id}";
            if (subjectWorkerId.HasValue && subjectWorkerId.Value > 0)
                url += "?subjectWorkerId=" + subjectWorkerId.Value;

            var dto = await _http.GetFromJsonAsync<TimesheetEntryEditDto>(url);
            if (dto == null) throw new InvalidOperationException("Timesheet entry edit response was empty.");
            return dto;
        }

        public async Task UpdateTimesheetEntryAsync(int id, UpdateTimesheetEntryDto dto, int? subjectWorkerId = null)
        {
            var url = $"api/timesheet/entries/{id}";
            if (subjectWorkerId.HasValue && subjectWorkerId.Value > 0)
                url += "?subjectWorkerId=" + subjectWorkerId.Value;

            var resp = await _http.PutAsJsonAsync(url, dto);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Update timesheet entry failed ({(int)resp.StatusCode}): {body}");
            }
        }

        public async Task DeleteTimesheetEntryAsync(int id, int? subjectWorkerId = null)
        {
            var url = $"api/timesheet/entries/{id}";
            if (subjectWorkerId.HasValue && subjectWorkerId.Value > 0)
                url += "?subjectWorkerId=" + subjectWorkerId.Value;

            var resp = await _http.DeleteAsync(url);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Delete timesheet entry failed ({(int)resp.StatusCode}): {body}");
            }
        }

        public async Task<List<ProjectCcfRefDetailsDto>> GetProjectCcfRefsByJobKeyAsync(string jobKey, bool includeInactive = false)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}/ccf-refs?includeInactive={(includeInactive ? "true" : "false")}";
            return await _http.GetFromJsonAsync<List<ProjectCcfRefDetailsDto>>(url) ?? new List<ProjectCcfRefDetailsDto>();
        }

        public async Task<ProjectCcfRefDetailsDto> CreateProjectCcfRefByJobKeyAsync(string jobKey, string code)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}/ccf-refs";
            var resp = await _http.PostAsJsonAsync(url, new CreateProjectCcfRefDto(code));
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<ProjectCcfRefDetailsDto>())!;
        }

        public async Task<ProjectCcfRefDetailsDto> SetProjectCcfRefActiveByJobKeyAsync(string jobKey, int id, bool isActive)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}/ccf-refs/{id}";
            var resp = await _http.PatchAsJsonAsync(url, isActive);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<ProjectCcfRefDetailsDto>())!;
        }

        public async Task<ProjectCcfRefDetailsDto> UpdateProjectCcfRefByJobKeyAsync(string jobKey, int id, UpdateProjectCcfRefDto dto)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}/ccf-refs/{id}";
            var resp = await _http.PutAsJsonAsync(url, dto);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<ProjectCcfRefDetailsDto>())!;
        }

        public async Task SetProjectCcfRefDeletedByJobKeyAsync(string jobKey, int id, bool isDeleted)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}/ccf-refs/{id}/deleted";
            var resp = await _http.PatchAsJsonAsync(url, isDeleted);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Delete CCF Ref failed ({(int)resp.StatusCode}): {body}");
            }
        }

        public async Task<ProjectEditDto> GetProjectForEditAsync(string jobKey)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}/edit";
            var result = await _http.GetFromJsonAsync<ProjectEditDto>(url);

            if (result == null)
                throw new InvalidOperationException("Project edit response was empty.");

            return result;
        }

        public async Task UpdateProjectAsync(string jobKey, UpdateProjectRequestDto dto)
        {
            var url = $"api/projects/{Uri.EscapeDataString(jobKey)}";
            var resp = await _http.PutAsJsonAsync(url, dto);
            resp.EnsureSuccessStatusCode(); // expects 204 NoContent
        }

        private static string ComputeDesktopVersionString()
        {
            // 1) Packaged: try to find AppxManifest.xml near the running app and read Identity Version
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);

                // Walk up a few levels from the deployed executable folder
                for (int i = 0; i < 10 && dir != null; i++)
                {
                    var manifestPath = Path.Combine(dir.FullName, "AppxManifest.xml");
                    if (File.Exists(manifestPath))
                    {
                        var doc = XDocument.Load(manifestPath);

                        var identity = doc.Descendants()
                            .FirstOrDefault(e => e.Name.LocalName == "Identity");

                        var v = identity?.Attribute("Version")?.Value;
                        if (!string.IsNullOrWhiteSpace(v))
                            return v.Trim();
                    }

                    dir = dir.Parent;
                }
            }
            catch
            {
                // ignore and fall back
            }

            // 2) Fallback: assembly version (unpackaged)
            var asm = typeof(ErpApiClient).Assembly;
            return asm.GetName().Version?.ToString() ?? "0.0.0.0";
        }

        private static string GetDesktopVersionString() => _desktopVersion.Value;

        private void SetClientHeaders()
        {
            const string appHeader = "X-Client-App";
            const string verHeader = "X-Client-Version";

            if (_http.DefaultRequestHeaders.Contains(appHeader))
                _http.DefaultRequestHeaders.Remove(appHeader);
            if (_http.DefaultRequestHeaders.Contains(verHeader))
                _http.DefaultRequestHeaders.Remove(verHeader);

            _http.DefaultRequestHeaders.Add(appHeader, "Desktop");
            _http.DefaultRequestHeaders.Add(verHeader, GetDesktopVersionString());
        }

        private static async Task ThrowFriendlyIfUpgradeRequiredAsync(HttpResponseMessage resp)
        {
            if ((int)resp.StatusCode != 426) return;

            var body = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                "A new version of the desktop app is available. Please close and re-open the app to update.\n\n" +
                "Details: " + body);
        }

        // -------------------------------
        // Applications
        // -------------------------------

        public async Task<List<ApplicationListEntryDto>> GetApplicationsAsync()
        {
            var result = await _http.GetFromJsonAsync<List<ApplicationListEntryDto>>("api/applications");
            return result ?? new List<ApplicationListEntryDto>();
        }

        public async Task<List<ApplicationProjectPicklistItemDto>> GetApplicationProjectPicklistAsync()
        {
            var result = await _http.GetFromJsonAsync<List<ApplicationProjectPicklistItemDto>>("api/applications/project-picklist");
            return result ?? new List<ApplicationProjectPicklistItemDto>();
        }

        public async Task<CreateApplicationResponseDto> CreateApplicationAsync(CreateApplicationRequestDto dto)
        {
            var resp = await _http.PostAsJsonAsync("api/applications", dto);

            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var msg = await resp.Content.ReadAsStringAsync();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg)
                    ? "An application with that number already exists."
                    : msg);
            }

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"API failed ({(int)resp.StatusCode}): {body}");
            }

            var created = await resp.Content.ReadFromJsonAsync<CreateApplicationResponseDto>();
            if (created == null) throw new InvalidOperationException("Create application response was empty.");

            return created;
        }

        public async Task<ApplicationDetailsDto> GetApplicationByIdAsync(int id)
        {
            var resp = await _http.GetAsync($"api/applications/{id}");
            resp.EnsureSuccessStatusCode();

            var dto = await resp.Content.ReadFromJsonAsync<ApplicationDetailsDto>();
            if (dto == null) throw new Exception("Failed to deserialize ApplicationDetailsDto.");

            return dto;
        }

        public async Task<UpdateApplicationResponseDto> UpdateApplicationAsync(int id, UpdateApplicationRequestDto dto)
        {
            var resp = await _http.PutAsJsonAsync($"api/applications/{id}", dto);
            resp.EnsureSuccessStatusCode();

            var result = await resp.Content.ReadFromJsonAsync<UpdateApplicationResponseDto>();
            if (result == null) throw new Exception("Failed to deserialize UpdateApplicationResponseDto.");

            return result;
        }

        public async Task LinkInvoiceToApplicationAsync(int applicationId, string invoiceNumber)
        {
            var url = $"api/applications/{applicationId}/link-invoice";

            var resp = await _http.PostAsJsonAsync(url, new { invoiceNumber });

            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var msg = await resp.Content.ReadAsStringAsync();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg) ? "Invoice is already linked." : msg);
            }

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"API failed ({(int)resp.StatusCode}): {body}");
            }
        }

        public async Task<BackfillInvoiceProjectLinksResultDto> BackfillInvoiceProjectLinksAsync(bool dryRun = true, int take = 5000)
        {
            var resp = await _http.PostAsync($"api/invoices/admin/backfill-project-links?dryRun={dryRun}&take={take}", content: null);
            resp.EnsureSuccessStatusCode();

            var dto = await resp.Content.ReadFromJsonAsync<BackfillInvoiceProjectLinksResultDto>();
            if (dto == null) throw new Exception("Failed to deserialize BackfillInvoiceProjectLinksResultDto.");

            return dto;
        }
    }
}
