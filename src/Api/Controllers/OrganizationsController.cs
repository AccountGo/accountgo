#nullable disable
using System;
using System.Linq;
using System.Security.Claims;
using Api.Data;
using Api.Filters;
using Api.Service;
using Core.Domain;
using Core.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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
        private readonly CompanyAccountingPolicyService _accountingPolicy;
        private string UserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

        public OrganizationsController(
            ITenantContext tenant,
            ApiDbContext db,
            WorkspaceService workspaces,
            CompanyAccountingPolicyService accountingPolicy)
        {
            _tenant = tenant;
            _db = db;
            _workspaces = workspaces;
            _accountingPolicy = accountingPolicy;
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
            public string Plan { get; set; } = "accounting_free";
            public string Currency { get; set; } = "USD";
            public string LegalName { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public ReportingFramework? ReportingFramework { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public AccountingBasis? AccountingBasis { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public FiscalYearPattern? FiscalYearPattern { get; set; }
            public int? FiscalYearStartMonth { get; set; }
            public DateTime? BooksStartDate { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public TaxRegime? TaxRegime { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public IndustryTemplate? IndustryTemplate { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public InventoryCostingMethod? InventoryCostingMethod { get; set; }
        }

        /// <summary>Omitted policy fields fall back to the legacy defaults documented on <see cref="CompanyProvisionOptions"/>.</summary>
        private static CompanyProvisionOptions ToOptions(CreateOrganizationRequest request)
        {
            var options = new CompanyProvisionOptions
            {
                LegalName = request.LegalName,
                FiscalYearStartMonth = request.FiscalYearStartMonth,
                BooksStartDate = request.BooksStartDate?.Date
            };
            if (request.ReportingFramework is ReportingFramework framework) options.ReportingFramework = framework;
            // Cash basis framework implies Cash basis; every other framework implies Accrual unless the client says otherwise.
            options.AccountingBasis = request.AccountingBasis
                ?? (options.ReportingFramework == ReportingFramework.CashBasis ? AccountingBasis.Cash : AccountingBasis.Accrual);
            if (request.FiscalYearPattern is FiscalYearPattern pattern) options.FiscalYearPattern = pattern;
            if (request.TaxRegime is TaxRegime regime) options.TaxRegime = regime;
            if (request.IndustryTemplate is IndustryTemplate industry) options.IndustryTemplate = industry;
            if (request.InventoryCostingMethod is InventoryCostingMethod costing) options.InventoryCostingMethod = costing;
            return options;
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
            var plan = Company.NormalizePlanKey(request.Plan ?? "accounting_free");
            if (slug.Length == 0 || slug.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(currency, "^[A-Z]{3}$") ||
                !Company.IsKnownPlan(plan))
                return Problem(statusCode: 400, title: "Invalid workspace settings");

            var options = ToOptions(request);
            var policyErrors = CompanyAccountingPolicyRules.Validate(options, DateTime.UtcNow);
            if (policyErrors.Count > 0)
                return Problem(statusCode: 400, title: "Invalid accounting policy", detail: string.Join(" ", policyErrors));

            using (var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable))
            {
                if (_workspaces.SlugExists(slug))
                    return Problem(statusCode: 409, title: "Workspace Slug Already Taken");
                try
                {
                    var company = _workspaces.Create(UserId, request.Name, slug, plan, currency, request.ShortName, options);
                    tx.Commit();
                    return StatusCode(201, Summary(company, CompanyMemberRole.Owner));
                }
                catch (InvalidAccountingPolicyException ex)
                {
                    return Problem(statusCode: 400, title: "Invalid accounting policy", detail: string.Join(" ", ex.Errors));
                }
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
                    if (setting != null && setting.Currency != currency && _accountingPolicy.IsLedgerFrozen(company.Id))
                        return Problem(statusCode: 409, title: "Currency is locked",
                            detail: "The workspace currency cannot change after the first journal entry is posted.");
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
        [Authorize(Policy = "MfaStepUp")]
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

        public class PostalAddressModel
        {
            public string Line1 { get; set; }
            public string Line2 { get; set; }
            public string City { get; set; }
            public string Region { get; set; }
            public string PostalCode { get; set; }
            public string CountryCode { get; set; }
        }

        public class BusinessProfileModel
        {
            public string LegalName { get; set; }
            public string BusinessEmail { get; set; }
            public string BusinessPhone { get; set; }
            public PostalAddressModel Address { get; set; }
        }

        public class SalesInvoiceSettingsModel
        {
            public string SellerName { get; set; } = SalesInvoiceSettings.TradingName;
            public bool ShowSellerAddress { get; set; } = true;
            public bool ShowCustomerEmail { get; set; } = true;
            public bool ShowNotes { get; set; } = true;
            public bool ShowPaymentInstructions { get; set; } = true;
            public string DefaultNotes { get; set; }
            public string DefaultPaymentInstructions { get; set; }
        }

        [TenantScoped]
        [HttpGet("current/business-profile")]
        public IActionResult GetBusinessProfile()
        {
            return Ok(ToBusinessProfileModel(_db.CompanySettings.FirstOrDefault()));
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPut("current/business-profile")]
        public IActionResult UpdateBusinessProfile([FromBody] BusinessProfileModel request)
        {
            if (request == null)
                return Problem(statusCode: 400, title: "Business profile required");

            var email = Clean(request.BusinessEmail);
            if (email != null && !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
                return Problem(statusCode: 400, title: "Invalid business email");

            var address = new PostalAddress
            {
                Line1 = Clean(request.Address?.Line1),
                Line2 = Clean(request.Address?.Line2),
                City = Clean(request.Address?.City),
                Region = Clean(request.Address?.Region),
                PostalCode = Clean(request.Address?.PostalCode),
                CountryCode = Clean(request.Address?.CountryCode),
            };

            var setting = GetOrCreateSetting();
            setting.LegalName = Clean(request.LegalName);
            setting.BusinessEmail = email;
            setting.BusinessPhone = Clean(request.BusinessPhone);
            setting.BusinessAddress = address.IsEmpty ? null : address;
            _db.SaveChanges();

            return Ok(ToBusinessProfileModel(setting));
        }

        // Read by every member: the invoice editor prefills new invoices from the default texts.
        [TenantScoped]
        [HttpGet("current/sales-invoice-settings")]
        public IActionResult GetSalesInvoiceSettings()
        {
            var settings = _db.CompanySettings.FirstOrDefault()?.GetSalesInvoiceSettings() ?? new SalesInvoiceSettings();
            return Ok(ToSalesInvoiceSettingsModel(settings));
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPut("current/sales-invoice-settings")]
        public IActionResult UpdateSalesInvoiceSettings([FromBody] SalesInvoiceSettingsModel request)
        {
            if (request == null)
                return Problem(statusCode: 400, title: "Sales invoice settings required");
            if (!SalesInvoiceSettings.IsValidSellerName(request.SellerName))
                return Problem(statusCode: 400, title: "Invalid seller name",
                    detail: $"Seller name must be '{SalesInvoiceSettings.TradingName}' or '{SalesInvoiceSettings.LegalName}'.");

            var defaultNotes = Clean(request.DefaultNotes);
            var defaultPaymentInstructions = Clean(request.DefaultPaymentInstructions);
            if (defaultNotes?.Length > SalesInvoiceSettings.MaxDefaultTextLength
                || defaultPaymentInstructions?.Length > SalesInvoiceSettings.MaxDefaultTextLength)
                return Problem(statusCode: 400, title: "Default text too long",
                    detail: $"Default notes and payment instructions are limited to {SalesInvoiceSettings.MaxDefaultTextLength} characters.");

            var settings = new SalesInvoiceSettings
            {
                SellerName = request.SellerName,
                ShowSellerAddress = request.ShowSellerAddress,
                ShowCustomerEmail = request.ShowCustomerEmail,
                ShowNotes = request.ShowNotes,
                ShowPaymentInstructions = request.ShowPaymentInstructions,
                DefaultNotes = defaultNotes,
                DefaultPaymentInstructions = defaultPaymentInstructions,
            };

            var setting = GetOrCreateSetting();
            setting.SetSalesInvoiceSettings(settings);
            _db.SaveChanges();

            return Ok(ToSalesInvoiceSettingsModel(settings));
        }

        public class AccountingPolicyModel
        {
            [JsonConverter(typeof(StringEnumConverter))] public ReportingFramework? ReportingFramework { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public AccountingBasis? AccountingBasis { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public FiscalYearPattern? FiscalYearPattern { get; set; }
            public int? FiscalYearStartMonth { get; set; }
            public DateTime? BooksStartDate { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public TaxRegime? TaxRegime { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public IndustryTemplate? IndustryTemplate { get; set; }
            [JsonConverter(typeof(StringEnumConverter))] public InventoryCostingMethod? InventoryCostingMethod { get; set; }
        }

        [TenantScoped]
        [HttpGet("current/accounting-policy")]
        public IActionResult GetAccountingPolicy()
        {
            return Ok(ToPolicyResponse(_db.CompanySettings.FirstOrDefault() ?? new CompanySetting { CompanyId = _tenant.RequireCompanyId() }));
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPut("current/accounting-policy")]
        public IActionResult UpdateAccountingPolicy([FromBody] AccountingPolicyModel request)
        {
            if (request == null)
                return Problem(statusCode: 400, title: "Accounting policy required");

            var setting = GetOrCreateSetting();
            var result = _accountingPolicy.Apply(setting, new AccountingPolicyChange
            {
                ReportingFramework = request.ReportingFramework,
                AccountingBasis = request.AccountingBasis,
                FiscalYearPattern = request.FiscalYearPattern,
                FiscalYearStartMonth = request.FiscalYearStartMonth,
                BooksStartDate = request.BooksStartDate,
                TaxRegime = request.TaxRegime,
                IndustryTemplate = request.IndustryTemplate,
                InventoryCostingMethod = request.InventoryCostingMethod,
            }, DateTime.UtcNow);

            if (result.Frozen)
                return Problem(statusCode: 409, title: "Accounting policy is locked",
                    detail: "Reporting framework, basis, fiscal year and books start date cannot change after the first journal entry is posted.");
            if (result.Errors.Count > 0)
                return Problem(statusCode: 400, title: "Invalid accounting policy", detail: string.Join(" ", result.Errors));

            _db.SaveChanges();
            return Ok(ToPolicyResponse(setting));
        }

        [TenantScoped]
        [RequireWorkspaceRole(CompanyMemberRole.Admin)]
        [HttpPost("current/onboarding-checklist/dismiss")]
        public IActionResult DismissOnboardingChecklist()
        {
            var setting = GetOrCreateSetting();
            setting.OnboardingChecklistDismissedAt ??= DateTime.UtcNow;
            _db.SaveChanges();
            return Ok(ToPolicyResponse(setting));
        }

        private object ToPolicyResponse(CompanySetting s) => new
        {
            reportingFramework = s.ReportingFramework.ToString(),
            accountingBasis = s.AccountingBasis.ToString(),
            fiscalYearPattern = s.FiscalYearPattern.ToString(),
            fiscalYearStartMonth = s.FiscalYearStartMonth,
            booksStartDate = s.BooksStartDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            taxRegime = s.TaxRegime.ToString(),
            industryTemplate = s.IndustryTemplate.ToString(),
            inventoryCostingMethod = s.InventoryCostingMethod.ToString(),
            coaTemplateId = s.CoaTemplateId,
            currency = s.Currency,
            isLedgerFrozen = _accountingPolicy.IsLedgerFrozen(s.CompanyId),
            onboardingChecklistDismissedAt = s.OnboardingChecklistDismissedAt,
        };

        private CompanySetting GetOrCreateSetting()
        {
            var setting = _db.CompanySettings.FirstOrDefault();
            if (setting == null)
            {
                setting = new CompanySetting { CompanyId = _tenant.RequireCompanyId() };
                _db.CompanySettings.Add(setting);
            }
            return setting;
        }

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static BusinessProfileModel ToBusinessProfileModel(CompanySetting setting)
        {
            var address = setting?.BusinessAddress;
            return new BusinessProfileModel
            {
                LegalName = setting?.LegalName,
                BusinessEmail = setting?.BusinessEmail,
                BusinessPhone = setting?.BusinessPhone,
                Address = new PostalAddressModel
                {
                    Line1 = address?.Line1,
                    Line2 = address?.Line2,
                    City = address?.City,
                    Region = address?.Region,
                    PostalCode = address?.PostalCode,
                    CountryCode = address?.CountryCode,
                },
            };
        }

        private static SalesInvoiceSettingsModel ToSalesInvoiceSettingsModel(SalesInvoiceSettings settings) => new()
        {
            SellerName = settings.SellerName,
            ShowSellerAddress = settings.ShowSellerAddress,
            ShowCustomerEmail = settings.ShowCustomerEmail,
            ShowNotes = settings.ShowNotes,
            ShowPaymentInstructions = settings.ShowPaymentInstructions,
            DefaultNotes = settings.DefaultNotes,
            DefaultPaymentInstructions = settings.DefaultPaymentInstructions,
        };

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
