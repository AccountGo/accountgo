#nullable enable
using System;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Api.Authorization;
using Api.Data;
using Api.Filters;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Services.Financial;

namespace Api.Controllers;

[TenantScoped]
[Route("api/Financials/Partners")]
[RequireModule("financials")]
public sealed class PartnersController : BaseController
{
    private readonly ApiDbContext _db;
    private readonly PartnerService _service;
    private readonly ITenantContext _tenant;
    private readonly IAccessAuthorizationService _authorization;

    public PartnersController(ApiDbContext db, PartnerService service, ITenantContext tenant, IAccessAuthorizationService authorization)
    { _db = db; _service = service; _tenant = tenant; _authorization = authorization; }

    private bool Authorize(string operation, string permission, out IActionResult? denied)
    {
        var decision = _authorization.Authorize(operation, new AccessResourceContext());
        if (decision.Allowed) { denied = null; return true; }
        if (decision.ReasonCode == AccessReasonCode.PermissionDenied) denied = RequirePermissionAttribute.Denied(decision.MissingPermission ?? permission, decision.PolicyId, decision.PolicyVersion);
        else { var problem = new ProblemDetails { Title = decision.SafeTitle, Detail = decision.SafeMessage, Status = decision.HttpStatus }; denied = new ObjectResult(problem) { StatusCode = decision.HttpStatus }; }
        return false;
    }

    [HttpGet]
    [RequirePermission(SystemPermissions.FinancialsAccountsView)]
    public IActionResult Get([FromQuery] DateTime? asOf = null)
    {
        if (!Authorize(AccessOperationCatalog.FinancialsAccountsView, SystemPermissions.FinancialsAccountsView, out var denied)) return denied!;
        var setting = _service.Setting;
        if (setting.EntityType != EntityType.Partnership) return BadRequest(new { message = "Partners are only available for partnerships." });
        var partners = _service.Partners.OrderBy(p => p.Id).ToList().Select(p =>
        {
            var capital = _db.Accounts.First(a => a.Id == p.CapitalAccountId); var drawings = _db.Accounts.First(a => a.Id == p.DrawingsAccountId);
            return new { p.Id, p.Name, CapitalAccountCode = capital.AccountCode, CapitalAccountName = capital.AccountName, DrawingsAccountCode = drawings.AccountCode, DrawingsAccountName = drawings.AccountName,
                CapitalBalance = _service.Balance(capital.Id, asOf?.Date ?? DateTime.MaxValue), DrawingsBalance = _service.Balance(drawings.Id, asOf?.Date ?? DateTime.MaxValue), Archived = p.ArchivedAt != null, RowVersion = Convert.ToBase64String(p.RowVersion) };
        }).ToList();
        var openYearRows = _db.FinancialYears.Where(y => y.ClosedAt == null).OrderBy(y => y.StartDate).ToList();
        var openYears = openYearRows.Select(y => new { y.FiscalYearCode, RowVersion = ShareVersion(y.Id), Shares = _db.PartnerShares.Where(s => s.FinancialYearId == y.Id).Select(s => new { s.PartnerId, s.Percent }).ToList() }).ToList();
        var permanent = _db.Accounts.FirstOrDefault(a => a.AccountCode == "30100"); var distribution = _db.Accounts.FirstOrDefault(a => a.AccountCode == "30800");
        var balanceDate = asOf?.Date ?? DateTime.MaxValue;
        var capitalBalance = permanent == null ? 0m : _service.Balance(permanent.Id, balanceDate);
        var drawingsBalance = distribution == null ? 0m : _service.Balance(distribution.Id, balanceDate);
        return Ok(new { partnerAccountsFrom = setting.PartnerAccountsFrom, capitalBalance, drawingsBalance, partners, openYears });
    }

    public sealed record SetupRow(string Name, decimal Percent, decimal Capital, decimal Drawings);
    public sealed record SetupRequest(DateTime Date, SetupRow[] Partners);
    [HttpPost("Setup")]
    [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
    public IActionResult Setup([FromBody] SetupRequest request)
    {
        if (!Authorize(AccessOperationCatalog.WorkspaceAccountingPolicyUpdate, SystemPermissions.FinancialsAccountsManage, out var denied)) return denied!;
        try { using var tx = WriteTransaction(); _service.Setup(request.Date, request.Partners.Select(p => (p.Name, p.Percent, p.Capital, p.Drawings)).ToArray(), GetUserNameFromRequestHeader() ?? "unknown"); tx.Commit(); return Ok(GetResult()); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return BadRequest(new { message = ex.Message }); }
    }

    public sealed record AddRequest(string Name, string FiscalYearCode, decimal Percent, ShareRequest[] Shares);
    [HttpPost]
    [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
    public IActionResult Add([FromBody] AddRequest request)
    {
        if (!Authorize(AccessOperationCatalog.WorkspaceAccountingPolicyUpdate, SystemPermissions.FinancialsAccountsManage, out var denied)) return denied!;
        var targetYear = _db.FinancialYears.FirstOrDefault(y => y.FiscalYearCode == request.FiscalYearCode);
        if (targetYear == null) return NotFound();
        try
        {
            using var tx = WriteTransaction(); var setting = _service.Setting;
            if (!setting.PartnerAccountsFrom.HasValue) throw new InvalidOperationException("Set up partner accounts first.");
            var year = targetYear;
            var codes = _service.CreateAccounts(request.Name.Trim());
            var partner = new Partner { CompanyId = setting.CompanyId, Name = request.Name.Trim(), CapitalAccountId = _db.Accounts.First(a => a.AccountCode == codes.CapitalCode).Id, DrawingsAccountId = _db.Accounts.First(a => a.AccountCode == codes.DrawingsCode).Id, CreatedAt = DateTime.UtcNow, CreatedBy = GetUserNameFromRequestHeader() ?? "unknown", RowVersion = Guid.NewGuid().ToByteArray() };
            _db.Partners.Add(partner); _db.SaveChanges();
            var newShares = (request.Shares ?? Array.Empty<ShareRequest>()).Select(s => (PartnerId: s.PartnerId, Percent: s.Percent)).Append((PartnerId: partner.Id, Percent: request.Percent)).ToArray();
            _service.ValidateShares(year.Id, newShares); _db.PartnerShares.RemoveRange(_db.PartnerShares.Where(s => s.FinancialYearId == year.Id));
            _db.PartnerShares.AddRange(newShares.Select(s => new PartnerShare { CompanyId = setting.CompanyId, FinancialYearId = year.Id, PartnerId = s.PartnerId, Percent = s.Percent }));
            _db.SaveChanges(); tx.Commit(); return Ok(GetResult());
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return BadRequest(new { message = ex.Message }); }
    }

    public sealed record RenameRequest(string Name, string RowVersion);
    [HttpPut("{id:int}")]
    [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
    public IActionResult Rename(int id, [FromBody] RenameRequest request)
    {
        if (!Authorize(AccessOperationCatalog.WorkspaceAccountingPolicyUpdate, SystemPermissions.FinancialsAccountsManage, out var denied)) return denied!;
        var partner = _db.Partners.FirstOrDefault(p => p.Id == id); if (partner == null) return NotFound();
        try { if (!partner.RowVersion.SequenceEqual(Convert.FromBase64String(request.RowVersion))) return Conflict(new { message = "Someone else changed this. Reload and try again." }); } catch (FormatException) { return Conflict(new { message = "Someone else changed this. Reload and try again." }); }
        try { _service.ValidateName(request.Name, id); } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        using var tx = WriteTransaction(); partner.Name = request.Name.Trim(); partner.RowVersion = Guid.NewGuid().ToByteArray(); _db.Accounts.First(a => a.Id == partner.CapitalAccountId).AccountName = "Capital – " + partner.Name; _db.Accounts.First(a => a.Id == partner.DrawingsAccountId).AccountName = "Drawings – " + partner.Name;
        try { _db.SaveChanges(); tx.Commit(); return Ok(GetResult()); } catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Someone else changed this. Reload and try again." }); } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    public sealed record ShareRequest(int PartnerId, decimal Percent);
    [HttpPut("Shares/{fiscalYearCode}")]
    [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
    public IActionResult ReplaceShares(string fiscalYearCode, [FromBody] ShareRequest[] request, [FromHeader(Name = "If-Match")] string? rowVersion = null)
    {
        if (!Authorize(AccessOperationCatalog.WorkspaceAccountingPolicyUpdate, SystemPermissions.FinancialsAccountsManage, out var denied)) return denied!;
        var year = _db.FinancialYears.FirstOrDefault(y => y.FiscalYearCode == fiscalYearCode); if (year == null) return NotFound();
        try { _service.ValidateShares(year.Id, request.Select(x => (x.PartnerId, x.Percent)).ToArray());
            if (!string.IsNullOrWhiteSpace(rowVersion) && !string.Equals(rowVersion.Trim('"'), ShareVersion(year.Id), StringComparison.Ordinal)) return Conflict(new { message = "Someone else changed this. Reload and try again." });
            using var tx = WriteTransaction(); _db.PartnerShares.RemoveRange(_db.PartnerShares.Where(s => s.FinancialYearId == year.Id)); _db.PartnerShares.AddRange(request.Select(x => new PartnerShare { CompanyId = _tenant.RequireCompanyId(), FinancialYearId = year.Id, PartnerId = x.PartnerId, Percent = x.Percent })); _db.SaveChanges(); tx.Commit(); return Ok(GetResult()); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); } catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("{id:int}/Archive")]
    [RequirePermission(SystemPermissions.FinancialsAccountsManage)]
    public IActionResult Archive(int id)
    {
        if (!Authorize(AccessOperationCatalog.WorkspaceAccountingPolicyUpdate, SystemPermissions.FinancialsAccountsManage, out var denied)) return denied!;
        var p = _db.Partners.FirstOrDefault(x => x.Id == id); if (p == null) return NotFound();
        if (_service.Balance(p.CapitalAccountId, DateTime.MaxValue) != 0m || _service.Balance(p.DrawingsAccountId, DateTime.MaxValue) != 0m || _db.PartnerShares.Any(s => s.PartnerId == id && _db.FinancialYears.Any(y => y.Id == s.FinancialYearId && y.ClosedAt == null))) return BadRequest(new { message = "Partner accounts must have zero balances and no shares in open years before archiving." });
        using var tx = WriteTransaction(); p.ArchivedAt = DateTime.UtcNow; p.RowVersion = Guid.NewGuid().ToByteArray(); _db.SaveChanges(); tx.Commit(); return Ok(GetResult());
    }

    private object GetResult() => Get() is OkObjectResult ok ? ok.Value! : new { };
    private string ShareVersion(int yearId)
    {
        var canonical = yearId.ToString(CultureInfo.InvariantCulture) + ":" + string.Join(";", _db.PartnerShares.Where(s => s.FinancialYearId == yearId).OrderBy(s => s.PartnerId)
            .AsEnumerable().Select(s => s.PartnerId.ToString(CultureInfo.InvariantCulture) + "=" + s.Percent.ToString("0.0000", CultureInfo.InvariantCulture)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction WriteTransaction() => _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
}
