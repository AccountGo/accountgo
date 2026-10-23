#nullable disable
using System;
using System.Linq;
using System.Text.Json;
using System.Security.Claims;
using Api.Data;
using Api.Filters;
using Api.Service;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/organizations")]
    public class OrganizationsController : BaseController
    {
        private readonly ITenantContext _tenant;
        private readonly ApiDbContext _db;
        private readonly WorkspaceService _workspaces;
        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

        public OrganizationsController(ITenantContext tenant, ApiDbContext db, WorkspaceService workspaces)
        {
            _tenant = tenant;
            _db = db;
            _workspaces = workspaces;
        }

        [TenantFree]
        [HttpGet]
        public IActionResult List()
        {
            var rows = (from m in _db.CompanyMembers
                        join c in _db.Companies on m.CompanyId equals c.Id
                        where m.UserId == UserId && m.Status == CompanyMemberStatus.Active
                        orderby c.Name
                        select new { Company = c, m.Role }).ToList();
            return Ok(rows.Select(r => Summary(r.Company, r.Role)));
        }

        public class CreateOrganizationRequest
        {
            public string Name { get; set; } = "";
            public string ShortName { get; set; }
            public string Slug { get; set; }
            public string Plan { get; set; } = "accounting";
            public string Currency { get; set; } = "USD";
        }

        [TenantFree]
        [HttpPost]
        public IActionResult Create([FromBody] CreateOrganizationRequest request)
        {
            if (string.IsNullOrWhiteSpace(UserId))
                return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Name))
                return Problem(statusCode: 400, title: "Company Name Required", detail: "Company name is required.");
            var slug = WorkspaceService.NormalizeSlug(string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug);
            var currency = (request.Currency ?? "USD").Trim().ToUpperInvariant();
            var plan = (request.Plan ?? "accounting").ToLowerInvariant();
            if (slug.Length == 0 || slug.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(currency, "^[A-Z]{3}$") ||
                !new[] { "invoicing", "accounting", "enterprise" }.Contains(plan))
                return Problem(statusCode: 400, title: "Invalid workspace settings");

            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                if (_workspaces.SlugExists(slug))
                    return Problem(statusCode: 409, title: "Workspace Slug Already Taken");
                var company = _workspaces.Create(UserId, request.Name, slug, plan, currency, request.ShortName);
                tx.Commit();
                return StatusCode(201, Summary(company, CompanyMemberRole.Owner));
            }
        }

        [TenantScoped]
        [HttpGet("current")]
        public IActionResult GetCurrent()
        {
            var id = _tenant.RequireCompanyId();
            var company = _db.Companies.Single(c => c.Id == id);
            return Ok(new
            {
                id = company.Id, name = company.Name, slug = company.Slug, shortName = company.ShortName,
                plan = company.Plan, activeModules = company.GetActiveModules(), subscriptionStatus = company.SubscriptionStatus,
                role = _tenant.Role.ToString(), permissions = _tenant.Permissions,
                currency = _db.CompanySettings.Select(s => s.Currency).FirstOrDefault() ?? "USD"
            });
        }

        public class UpdateOrganizationRequest
        {
            public string Name { get; set; }
            public string Slug { get; set; }
            public string Currency { get; set; }
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPatch("current")]
        public IActionResult Update([FromBody] UpdateOrganizationRequest request)
        {
            var company = _db.Companies.Single(c => c.Id == _tenant.RequireCompanyId());
            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                if (request.Name != null)
                {
                    if (string.IsNullOrWhiteSpace(request.Name))
                        return Problem(statusCode: 400, title: "Company Name Required");
                    company.Name = request.Name.Trim();
                }
                if (request.Slug != null)
                {
                    var slug = WorkspaceService.NormalizeSlug(request.Slug);
                    if (slug.Length == 0 || slug.Length > 100)
                        return Problem(statusCode: 400, title: "Invalid workspace slug");
                    if (!string.Equals(slug, company.Slug, StringComparison.Ordinal) && _workspaces.SlugExists(slug))
                        return Problem(statusCode: 409, title: "Workspace Slug Already Taken");
                    company.Slug = slug;
                }
                if (request.Currency != null)
                {
                    var currency = request.Currency.Trim().ToUpperInvariant();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(currency, "^[A-Z]{3}$"))
                        return Problem(statusCode: 400, title: "Invalid currency");
                    var setting = _db.CompanySettings.FirstOrDefault();
                    if (setting == null)
                        _db.CompanySettings.Add(new CompanySetting { CompanyId = company.Id, Currency = currency });
                    else
                        setting.Currency = currency;
                }
                _db.SaveChanges();
                tx.Commit();
            }
            return Ok(Summary(company, _tenant.Role.Value));
        }

        public class DeleteOrganizationRequest
        {
            public string ConfirmSlug { get; set; }
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Owner)]
        [HttpDelete("current")]
        public IActionResult Delete([FromBody] DeleteOrganizationRequest request)
        {
            var company = _db.Companies.Single(c => c.Id == _tenant.RequireCompanyId());
            if (!string.Equals(request.ConfirmSlug, company.Slug, StringComparison.Ordinal))
                return Problem(statusCode: 400, title: "Workspace slug confirmation does not match");
            company.DeletedAt = DateTime.UtcNow;
            _db.SaveChanges();
            return NoContent();
        }

        // ===== Sales Invoice Settings =====

        public class UpdateSalesInvoiceSettingsRequest
        {
            public string? SellerName { get; set; }
            public string? LegalName { get; set; }
            public bool? ShowNotes { get; set; }
            public bool? ShowPaymentInstructions { get; set; }
            public bool? ShowCustomerEmail { get; set; }
            public string? DefaultNotes { get; set; }
            public string? DefaultPaymentInstructions { get; set; }
        }

        public class UpdateBusinessProfileRequest
        {
            public string? LegalName { get; set; }
            public string? BusinessEmail { get; set; }
            public string? BusinessPhone { get; set; }
            public string? BusinessAddressLine1 { get; set; }
            public string? BusinessAddressLine2 { get; set; }
            public string? BusinessAddressCity { get; set; }
            public string? BusinessAddressRegion { get; set; }
            public string? BusinessAddressPostalCode { get; set; }
            public string? BusinessAddressCountryCode { get; set; }
        }

        [TenantScoped]
        [HttpGet("current/sales-invoice-settings")]
        public IActionResult GetSalesInvoiceSettings()
        {
            var company = _db.Companies.Single(c => c.Id == _tenant.RequireCompanyId());
            var setting = _db.CompanySettings.FirstOrDefault(s => s.CompanyId == company.Id);
            var salesSettings = setting?.GetSalesInvoiceSettings();

            return Ok(new
            {
                sellerName = salesSettings?.SellerName,
                legalName = setting?.LegalName,
                showNotes = salesSettings?.ShowNotes ?? true,
                showPaymentInstructions = salesSettings?.ShowPaymentInstructions ?? true,
                showCustomerEmail = salesSettings?.ShowCustomerEmail ?? true,
                defaultNotes = salesSettings?.DefaultNotes,
                defaultPaymentInstructions = salesSettings?.DefaultPaymentInstructions
            });
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPatch("current/sales-invoice-settings")]
        public IActionResult UpdateSalesInvoiceSettings([FromBody] UpdateSalesInvoiceSettingsRequest request)
        {
            var company = _db.Companies.Single(c => c.Id == _tenant.RequireCompanyId());
            var setting = _db.CompanySettings.FirstOrDefault(s => s.CompanyId == company.Id) ?? new CompanySetting { CompanyId = company.Id };

            if (request.SellerName != null)
                setting.SalesInvoiceSettingsJson = UpdateSalesInvoiceSettingsJson(setting.SalesInvoiceSettingsJson, "SellerName", request.SellerName);
            if (request.LegalName != null)
                setting.LegalName = request.LegalName;
            if (request.ShowNotes.HasValue)
                setting.SalesInvoiceSettingsJson = UpdateSalesInvoiceSettingsJson(setting.SalesInvoiceSettingsJson, "ShowNotes", request.ShowNotes.Value.ToString());
            if (request.ShowPaymentInstructions.HasValue)
                setting.SalesInvoiceSettingsJson = UpdateSalesInvoiceSettingsJson(setting.SalesInvoiceSettingsJson, "ShowPaymentInstructions", request.ShowPaymentInstructions.Value.ToString());
            if (request.ShowCustomerEmail.HasValue)
                setting.SalesInvoiceSettingsJson = UpdateSalesInvoiceSettingsJson(setting.SalesInvoiceSettingsJson, "ShowCustomerEmail", request.ShowCustomerEmail.Value.ToString());
            if (request.DefaultNotes != null)
                setting.SalesInvoiceSettingsJson = UpdateSalesInvoiceSettingsJson(setting.SalesInvoiceSettingsJson, "DefaultNotes", request.DefaultNotes);
            if (request.DefaultPaymentInstructions != null)
                setting.SalesInvoiceSettingsJson = UpdateSalesInvoiceSettingsJson(setting.SalesInvoiceSettingsJson, "DefaultPaymentInstructions", request.DefaultPaymentInstructions);

            if (setting.Id == 0)
                _db.CompanySettings.Add(setting);

            _db.SaveChanges();

            var updatedSettings = setting.GetSalesInvoiceSettings();

            return Ok(new
            {
                sellerName = updatedSettings.SellerName,
                legalName = setting.LegalName,
                showNotes = updatedSettings.ShowNotes,
                showPaymentInstructions = updatedSettings.ShowPaymentInstructions,
                showCustomerEmail = updatedSettings.ShowCustomerEmail,
                defaultNotes = updatedSettings.DefaultNotes,
                defaultPaymentInstructions = updatedSettings.DefaultPaymentInstructions
            });
        }

        // Helper to update a JSON setting value by key
        private static string? UpdateSalesInvoiceSettingsJson(string? json, string key, string value)
        {
            // Normalize key to camelCase for JSON storage (matches JsonNamingPolicy.CamelCase)
            var camelKey = key switch
            {
                "SellerName" => "sellerName",
                "LegalName" => "legalName",
                "ShowNotes" => "showNotes",
                "ShowPaymentInstructions" => "showPaymentInstructions",
                "ShowCustomerEmail" => "showCustomerEmail",
                "DefaultNotes" => "defaultNotes",
                "DefaultPaymentInstructions" => "defaultPaymentInstructions",
                _ => key.ToLowerInvariant()
            };

            if (string.IsNullOrEmpty(json))
            {
                var dict = new Dictionary<string, string?> { [camelKey] = value };
                return JsonSerializer.Serialize(dict);
            }

            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
                if (dict == null) return json;
                dict[camelKey] = value;
                return JsonSerializer.Serialize(dict);
            }
            catch
            {
                var dict = new Dictionary<string, string?> { [camelKey] = value };
                return JsonSerializer.Serialize(dict);
            }
        }

        // ===== Business Profile =====

        [TenantScoped]
        [HttpGet("current/business-profile")]
        public IActionResult GetBusinessProfile()
        {
            var company = _db.Companies.Single(c => c.Id == _tenant.RequireCompanyId());
            var setting = _db.CompanySettings.FirstOrDefault(s => s.CompanyId == company.Id);

            var businessAddress = setting?.BusinessAddress;
            return Ok(new
            {
                legalName = setting?.LegalName,
                businessEmail = setting?.BusinessEmail,
                businessPhone = setting?.BusinessPhone,
                businessAddressLine1 = businessAddress?.Line1,
                businessAddressLine2 = businessAddress?.Line2,
                businessAddressCity = businessAddress?.City,
                businessAddressRegion = businessAddress?.Region,
                businessAddressPostalCode = businessAddress?.PostalCode,
                businessAddressCountryCode = businessAddress?.CountryCode
            });
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPatch("current/business-profile")]
        public IActionResult UpdateBusinessProfile([FromBody] UpdateBusinessProfileRequest request)
        {
            var company = _db.Companies.Single(c => c.Id == _tenant.RequireCompanyId());
            var setting = _db.CompanySettings.FirstOrDefault(s => s.CompanyId == company.Id) ?? new CompanySetting { CompanyId = company.Id };

            if (request.LegalName != null)
                setting.LegalName = request.LegalName;
            if (request.BusinessEmail != null)
                setting.BusinessEmail = request.BusinessEmail;
            if (request.BusinessPhone != null)
                setting.BusinessPhone = request.BusinessPhone;
            if (request.BusinessAddressLine1 != null)
                setting.BusinessAddress.Line1 = request.BusinessAddressLine1;
            if (request.BusinessAddressLine2 != null)
                setting.BusinessAddress.Line2 = request.BusinessAddressLine2;
            if (request.BusinessAddressCity != null)
                setting.BusinessAddress.City = request.BusinessAddressCity;
            if (request.BusinessAddressRegion != null)
                setting.BusinessAddress.Region = request.BusinessAddressRegion;
            if (request.BusinessAddressPostalCode != null)
                setting.BusinessAddress.PostalCode = request.BusinessAddressPostalCode;
            if (request.BusinessAddressCountryCode != null)
                setting.BusinessAddress.CountryCode = request.BusinessAddressCountryCode;

            // Ensure PostalAddress is created/updated
            if (setting.BusinessAddress == null && (request.BusinessAddressLine1 != null || request.BusinessAddressCity != null || request.BusinessAddressRegion != null || request.BusinessAddressPostalCode != null || request.BusinessAddressCountryCode != null))
            {
                setting.BusinessAddress = new PostalAddress();
                setting.BusinessAddress.Line1 = request.BusinessAddressLine1;
                setting.BusinessAddress.Line2 = request.BusinessAddressLine2;
                setting.BusinessAddress.City = request.BusinessAddressCity;
                setting.BusinessAddress.Region = request.BusinessAddressRegion;
                setting.BusinessAddress.PostalCode = request.BusinessAddressPostalCode;
                setting.BusinessAddress.CountryCode = request.BusinessAddressCountryCode;
            }
            else
            {
                if (request.BusinessAddressLine1 != null)
                    setting.BusinessAddress.Line1 = request.BusinessAddressLine1;
                if (request.BusinessAddressLine2 != null)
                    setting.BusinessAddress.Line2 = request.BusinessAddressLine2;
                if (request.BusinessAddressCity != null)
                    setting.BusinessAddress.City = request.BusinessAddressCity;
                if (request.BusinessAddressRegion != null)
                    setting.BusinessAddress.Region = request.BusinessAddressRegion;
                if (request.BusinessAddressPostalCode != null)
                    setting.BusinessAddress.PostalCode = request.BusinessAddressPostalCode;
                if (request.BusinessAddressCountryCode != null)
                    setting.BusinessAddress.CountryCode = request.BusinessAddressCountryCode;
            }
            if (request.BusinessAddressLine2 != null)
                setting.BusinessAddress.Line2 = request.BusinessAddressLine2;
            if (request.BusinessAddressCity != null)
                setting.BusinessAddress.City = request.BusinessAddressCity;
            if (request.BusinessAddressRegion != null)
                setting.BusinessAddress.Region = request.BusinessAddressRegion;
            if (request.BusinessAddressPostalCode != null)
                setting.BusinessAddress.PostalCode = request.BusinessAddressPostalCode;
            if (request.BusinessAddressCountryCode != null)
                setting.BusinessAddress.CountryCode = request.BusinessAddressCountryCode;

            if (setting.Id == 0)
                _db.CompanySettings.Add(setting);

            _db.SaveChanges();

            var updatedSetting = _db.CompanySettings.FirstOrDefault(s => s.CompanyId == company.Id);
            var businessAddress2 = updatedSetting?.BusinessAddress;

            return Ok(new
            {
                legalName = updatedSetting?.LegalName,
                businessEmail = updatedSetting?.BusinessEmail,
                businessPhone = updatedSetting?.BusinessPhone,
                businessAddressLine1 = businessAddress2?.Line1,
                businessAddressLine2 = businessAddress2?.Line2,
                businessAddressCity = businessAddress2?.City,
                businessAddressRegion = businessAddress2?.Region,
                businessAddressPostalCode = businessAddress2?.PostalCode,
                businessAddressCountryCode = businessAddress2?.CountryCode
            });
        }

        private static object Summary(Company company, CompanyMemberRole role)
        {
            return new
            {
                id = company.Id.ToString(), name = company.Name, shortName = company.ShortName, slug = company.Slug,
                plan = company.Plan, subscriptionStatus = company.SubscriptionStatus,
                activeModules = company.GetActiveModules(), role = role.ToString()
            };
        }
    }
}
