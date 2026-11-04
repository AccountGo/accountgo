using Dto.Administration;
using Dto.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Services.Administration;
using Services.Billing;
using Services.Financial;
using Services.Inventory;
using Services.Purchasing;
using Services.Sales;
using Services.Security;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Api.Data;

namespace Api.Controllers
{
    [Core.Tenancy.TenantScoped]
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AdministrationController : BaseController
    {
        // Global role-template management is platform-staff only. User CRUD routes are retired.
        private const string PlatformAdminRole = "SystemAdministrators";

        private readonly IAdministrationService _adminService;
        private readonly IFinancialService _financialService;
        private readonly ISalesService _salesService;
        private readonly IPurchasingService _purchasingService;
        private readonly IInventoryService _inventoryService;
        private readonly ISecurityService _securityService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly Core.Tenancy.ITenantContext _tenantContext;
        private readonly IPlanCatalog _plans;

        public AdministrationController(IAdministrationService adminService,
            IFinancialService financialService,
            ISalesService salesService,
            IPurchasingService purchasingService,
            IInventoryService inventoryService,
            ISecurityService securityService,
            UserManager<ApplicationUser> userManager,
            Core.Tenancy.ITenantContext tenantContext,
            IPlanCatalog plans)
        {
            _adminService = adminService;
            _financialService = financialService;
            _salesService = salesService;
            _purchasingService = purchasingService;
            _inventoryService = inventoryService;
            _securityService = securityService;
            _userManager = userManager;
            _tenantContext = tenantContext;
            _plans = plans;
        }

        [Api.Filters.RequireWorkspaceRole(Core.Domain.CompanyMemberRole.Owner)]
        [HttpGet]
        [Route("Setup")]
        public IActionResult Setup([FromServices] Api.Service.CompanyProvisioningService provisioning, [FromServices] ApiDbContext db)
        {
            var company = db.Companies.Single(c => c.Id == _tenantContext.RequireCompanyId());
            using (var tx = db.Database.BeginTransaction())
            {
                provisioning.Provision(company);
                tx.Commit();
            }
            return Ok(new { message = "Workspace defaults initialized." });
        }

        public class ClearDatabaseRequest
        {
            /// <summary>Must be the literal string "DELETE_ALL_DATA" to proceed.</summary>
            public string? Confirmation { get; set; }
        }

        [Authorize(Roles = PlatformAdminRole)]
        [HttpPost]
        [Route("clear")]
        public IActionResult Clear([FromBody] ClearDatabaseRequest? request)
        {
            if (!string.Equals(request?.Confirmation, "DELETE_ALL_DATA", StringComparison.Ordinal))
            {
                return BadRequest(new
                {
                    message = "Confirmation required. Send JSON body { \"confirmation\": \"DELETE_ALL_DATA\" }."
                });
            }

            Api.Data.Initializer initializer = new Data.Initializer(_adminService, _financialService, _salesService, _purchasingService, _inventoryService, _securityService, _plans);
            bool success = initializer.Clear();
            if (success)
                return Ok(new { message = "Database is cleared!" });
            else
                return BadRequest(new { message = "Clearing database not successful! Please check the log." });
        }

        [HttpGet("company")]
        public IActionResult Company([FromQuery] string? slug = null, [FromQuery] string? companyCode = null, [FromQuery] int? id = null)
        {
            var companyId = _tenantContext.RequireCompanyId();
            var company = _adminService.GetCompanies().FirstOrDefault(c => c.Id == companyId);

            if (company == null)
            {
                return NotFound(new { message = "Company not found." });
            }

            return Ok(new
            {
                id = company.Id,
                name = company.Name,
                shortName = company.ShortName,
                slug = string.IsNullOrWhiteSpace(company.Slug) ? (company.ShortName?.ToLowerInvariant() ?? $"org-{company.Id}") : company.Slug,
                companyCode = company.CompanyCode,
                cra = company.CRA,
                plan = _plans.ResolveStoredPlan(company.Plan).Key,
                subscriptionStatus = company.SubscriptionStatus ?? "active",
                subscriptionValidUntil = company.SubscriptionValidUntil,
                activeModules = PlanModuleResolver.ResolveActiveModules(_plans, company)
            });
        }

        [HttpGet("financialyears")]
        public IActionResult FinancialYears()
        {
            var years = _adminService.GetFinancialYears()
                .Select(f => new
                {
                    id = f.Id,
                    fiscalYearCode = f.FiscalYearCode,
                    fiscalYearName = f.FiscalYearName,
                    startDate = f.StartDate,
                    endDate = f.EndDate,
                    isActive = f.IsActive
                })
                .ToList();
            // Read path: surface the year containing today without inserting rows.
            var current = _financialService.CurrentFiscalYear();
            if (current != null && years.All(y => y.fiscalYearCode != current.FiscalYearCode))
            {
                years.Add(new
                {
                    id = current.Id,
                    fiscalYearCode = current.FiscalYearCode,
                    fiscalYearName = current.FiscalYearName,
                    startDate = current.StartDate,
                    endDate = current.EndDate,
                    isActive = true
                });
            }
            return Ok(years);
        }

        [HttpGet("paymentterms")]
        public IActionResult PaymentTerms()
        {
            var terms = _adminService.GetPaymentTerms();
            return Ok(terms.Select(p => new
            {
                id = p.Id,
                description = p.Description,
                paymentType = p.PaymentType.ToString(),
                dueAfterDays = p.DueAfterDays,
                isActive = p.IsActive
            }));
        }

        // GET companies (listed every workspace) and POST company were removed in Phase 2.
        // Use GET/POST /api/organizations, which only lists the caller's memberships and makes the creator Owner.

        [Api.Filters.RequirePermission(Core.Domain.Security.SystemPermissions.AuditLogsView)]
        [HttpGet]
        [Route("auditlogs")] // api/Administration/AuditLogs
        public IActionResult AuditLogs()
        {
            var auditLogs = _adminService.AuditLogs();
            var auditLogsDto = auditLogs.Select(log => new AuditLog()
            {
                Id = log.Id,
                UserName = log.UserName,
                AuditEventDateUTC = log.AuditEventDateUTC,
                AuditEventType = log.AuditEventType,
                TableName = log.TableName,
                RecordId = log.RecordId,
                FieldName = log.FieldName,
                OriginalValue = log.OriginalValue,
                NewValue = log.NewValue
            }).ToList();

            return new ObjectResult(auditLogsDto);
        }

        // Global role templates incl. the platform role; workspaces use /api/organizations/current/security-roles.
        [Authorize(Roles = PlatformAdminRole)]
        [HttpGet]
        [Route("roles")] // api/Administration/Roles
        public IActionResult Roles()
        {
            var roles = _securityService.GetAllSecurityRole();
            var rolesDto = new List<Role>();

            foreach (var role in roles)
            {
                var roleDto = new Role
                {
                    Id = role.Id,
                    Name = role.Name,
                    DisplayName = role.DisplayName,
                    SysAdmin = role.SysAdmin
                };

                if (role.Permissions != null)
                {
                    foreach (var permission in role.Permissions)
                    {
                        if (permission?.SecurityPermission == null) continue;
                        var permissionDto = new Permission
                        {
                            Id = permission.SecurityPermissionId,
                            Name = permission.SecurityPermission.Name,
                            DisplayName = permission.SecurityPermission.DisplayName
                        };

                        roleDto.Permissions.Add(permissionDto);
                    }
                }

                rolesDto.Add(roleDto);
            }

            return Ok(rolesDto);
        }

        public class SaveRoleRequest
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string? DisplayName { get; set; }
            public List<int>? PermissionIds { get; set; }
        }

        [Authorize(Roles = PlatformAdminRole)]
        [HttpPost("roles")]
        public IActionResult SaveRole(
            [FromBody] SaveRoleRequest request,
            [FromServices] ApiDbContext db,
            [FromServices] Core.Authorization.IWorkspacePolicyTransaction? policyTransaction = null,
            [FromServices] Core.Authorization.IWorkspaceReviewerEligibility? reviewerEligibility = null)
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
                return BadRequest(new ProblemDetails { Title = "Invalid Role", Detail = "Role name is required.", Status = StatusCodes.Status400BadRequest });

            var displayName = !string.IsNullOrWhiteSpace(request.DisplayName) ? request.DisplayName.Trim() : request.Name.Trim();
            var permissionIds = request.PermissionIds ?? new List<int>();

            var affectedCompanyIds = request.Id > 0 && db != null
                ? db.CompanyMemberSecurityRoles
                    .Where(r => r.SecurityRoleId == request.Id)
                    .Select(r => r.CompanyMember.CompanyId)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList()
                : new List<int>();

            var permissionNames = affectedCompanyIds.Count > 0 && reviewerEligibility != null && db != null
                ? db.Set<Core.Domain.Security.SecurityPermission>()
                    .Where(p => permissionIds.Contains(p.Id))
                    .Select(p => p.Name)
                    .ToList()
                : null;

            if (affectedCompanyIds.Count > 0 && policyTransaction != null)
            {
                // Eligibility must re-check under the coordination lock so a concurrent member removal
                // cannot race past a pre-lock validation and leave independent review unusable.
                return policyTransaction.ExecuteAcrossCompanies(affectedCompanyIds, guards =>
                {
                    if (reviewerEligibility != null && permissionNames != null)
                    {
                        foreach (var companyId in affectedCompanyIds)
                        {
                            var verdict = reviewerEligibility.EvaluateRolePermissionChange(
                                companyId, request.Id, permissionNames);
                            if (verdict.Blocked)
                            {
                                return StatusCode(StatusCodes.Status409Conflict, new ProblemDetails
                                {
                                    Title = "Role change blocked",
                                    Detail = verdict.Message,
                                    Status = StatusCodes.Status409Conflict,
                                    Extensions =
                                    {
                                        ["reasonCode"] = verdict.ReasonCode,
                                        ["companyId"] = companyId
                                    }
                                });
                            }
                        }
                    }

                    foreach (var guard in guards)
                        guard.AdvanceRevision();
                    _securityService.SaveRoleWithPermissions(request.Id, request.Name.Trim(), displayName, permissionIds);
                    return Ok(new { success = true });
                });
            }

            _securityService.SaveRoleWithPermissions(request.Id, request.Name.Trim(), displayName, permissionIds);
            return Ok(new { success = true });
        }

        [Authorize(Roles = PlatformAdminRole)]
        [HttpDelete("roles/{id:int}")]
        public IActionResult DeleteRole(
            int id,
            [FromServices] ApiDbContext db,
            [FromServices] Core.Authorization.IWorkspacePolicyTransaction? policyTransaction = null,
            [FromServices] Core.Authorization.IWorkspaceReviewerEligibility? reviewerEligibility = null)
        {
            var role = _securityService.GetSecurityRole(id);
            if (role == null)
                return NotFound(new ProblemDetails { Title = "Role Not Found", Detail = $"Role with ID {id} was not found.", Status = StatusCodes.Status404NotFound });

            if (role.SysAdmin
                || role.Name == "SystemAdministrators"
                || role.Name == "GeneralUsers"
                || role.Name == Core.Domain.Security.WorkspaceAccessProfileKeys.FullWorkspaceAccessV1)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Action Denied",
                    Detail = "System-critical roles cannot be deleted.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var affectedCompanyIds = db != null
                ? db.CompanyMemberSecurityRoles
                    .Where(r => r.SecurityRoleId == id)
                    .Select(r => r.CompanyMember.CompanyId)
                    .Distinct()
                    .OrderBy(cId => cId)
                    .ToList()
                : new List<int>();

            if (affectedCompanyIds.Count > 0 && policyTransaction != null)
            {
                return policyTransaction.ExecuteAcrossCompanies(affectedCompanyIds, guards =>
                {
                    if (reviewerEligibility != null)
                    {
                        foreach (var companyId in affectedCompanyIds)
                        {
                            // Deleting the role removes all of its permissions from every assignee.
                            var verdict = reviewerEligibility.EvaluateRolePermissionChange(
                                companyId, id, Array.Empty<string>());
                            if (verdict.Blocked)
                            {
                                return StatusCode(StatusCodes.Status409Conflict, new ProblemDetails
                                {
                                    Title = "Role change blocked",
                                    Detail = verdict.Message,
                                    Status = StatusCodes.Status409Conflict,
                                    Extensions =
                                    {
                                        ["reasonCode"] = verdict.ReasonCode,
                                        ["companyId"] = companyId
                                    }
                                });
                            }
                        }
                    }

                    foreach (var guard in guards)
                        guard.AdvanceRevision();
                    _securityService.DeleteRole(id);
                    return Ok(new { success = true, message = "Role deleted successfully." });
                });
            }

            _securityService.DeleteRole(id);
            return Ok(new { success = true, message = "Role deleted successfully." });
        }

        private static User MapToUserDto(Core.Domain.Security.User user)
        {
            var dto = new User
            {
                Id = user.Id,
                FirstName = user.Firstname,
                LastName = user.Lastname,
                Email = user.EmailAddress,
                UserName = user.UserName
            };

            if (user.Roles != null)
            {
                foreach (var r in user.Roles)
                {
                    if (r?.SecurityRole == null) continue;
                    dto.Roles.Add(new Role
                    {
                        Id = r.SecurityRoleId,
                        Name = r.SecurityRole.Name,
                        DisplayName = r.SecurityRole.DisplayName,
                        SysAdmin = r.SecurityRole.SysAdmin
                    });
                }
            }

            return dto;
        }

        [Authorize(Roles = PlatformAdminRole)]
        [HttpGet]
        [Route("groups")]
        // TODO: generates exception
        public IActionResult Groups()
        {
            var groups = _securityService.GetAllSecurityGroup();
            var groupsDto = new List<Group>();

            foreach (var group in groups)
            {
                var groupDto = new Group
                {
                    Id = group.Id,
                    Name = group.Name,
                    DisplayName = group.DisplayName
                };

                foreach (var permission in group.Permissions)
                {
                    var permissionDto = new Permission
                    {
                        Id = permission.Id,
                        Name = permission.Name,
                        DisplayName = permission.DisplayName
                    };

                    groupDto.Permissions.Add(permissionDto);
                }

                groupsDto.Add(groupDto);
            }

            return new ObjectResult(groupsDto);
        }

        [Authorize(Roles = PlatformAdminRole)]
        [HttpGet]
        [Route("GetUser")]
        public IActionResult GetUser(string username)
        {
            var user = _securityService.GetUser(username);
            var userDto = new Dto.Security.User
            {
                Id = user.Id,
                FirstName = user.Firstname,
                LastName = user.Lastname,
                UserName = user.UserName,
                Email = user.EmailAddress
            };

            foreach (var role in user.Roles)
            {
                var roleDto = new Dto.Security.Role
                {
                    Id = role.SecurityRoleId,
                    Name = role.SecurityRole.Name,
                    SysAdmin = role.SecurityRole.SysAdmin
                };

                userDto.Roles.Add(roleDto);

                foreach (var permission in role.SecurityRole.Permissions)
                {
                    var permissionDto = new Dto.Security.Permission
                    {
                        Id = permission.SecurityPermissionId,
                        Name = permission.SecurityPermission.Name,
                        Group = new Dto.Security.Group
                        {
                            Name = permission.SecurityPermission.Group.Name
                        }
                    };
                    roleDto.Permissions.Add(permissionDto);
                }
            }

            return new ObjectResult(userDto);
        }

        public class UpdateCompanyRequest
        {
            public int? Id { get; set; }
            public string? Name { get; set; }
            public string? ShortName { get; set; }
            public string? Slug { get; set; }
            public string? CompanyCode { get; set; }
            public string? CRA { get; set; }
        }

        [Api.Filters.RequireWorkspaceRole(Core.Domain.CompanyMemberRole.Admin)]
        [HttpPost("SaveCompany")]
        [HttpPut("company")]
        public IActionResult SaveCompany([FromBody] UpdateCompanyRequest request)
        {
            if (request == null)
                return BadRequest(new { message = "Invalid company update request." });

            var companyId = _tenantContext.RequireCompanyId();
            var company = _adminService.GetCompanies().FirstOrDefault(c => c.Id == companyId);
            if (company == null)
            {
                return NotFound(new { message = "Company not found." });
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
                company.Name = request.Name.Trim();

            if (!string.IsNullOrWhiteSpace(request.ShortName))
                company.ShortName = request.ShortName.Trim();

            if (!string.IsNullOrWhiteSpace(request.Slug))
                company.Slug = request.Slug.Trim().ToLowerInvariant();

            if (request.CompanyCode != null)
                company.CompanyCode = request.CompanyCode.Trim();

            if (request.CRA != null)
                company.CRA = request.CRA.Trim();

            _adminService.SaveCompany(company);

            return Ok(new
            {
                id = company.Id,
                name = company.Name,
                shortName = company.ShortName,
                slug = company.Slug,
                companyCode = company.CompanyCode,
                cra = company.CRA,
                plan = _plans.ResolveStoredPlan(company.Plan).Key,
                subscriptionStatus = company.SubscriptionStatus,
                activeModules = PlanModuleResolver.ResolveActiveModules(_plans, company)
            });
        }
    }
}
