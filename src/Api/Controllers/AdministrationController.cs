using Dto.Administration;
using Dto.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Services.Administration;
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
using System.Text;
using System.Threading.Tasks;
using Api.Data;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdministrationController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly IFinancialService _financialService;
        private readonly ISalesService _salesService;
        private readonly IPurchasingService _purchasingService;
        private readonly IInventoryService _inventoryService;
        private readonly ISecurityService _securityService;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdministrationController(IAdministrationService adminService,
            IFinancialService financialService,
            ISalesService salesService,
            IPurchasingService purchasingService,
            IInventoryService inventoryService,
            ISecurityService securityService,
            UserManager<ApplicationUser> userManager)
        {
            _adminService = adminService;
            _financialService = financialService;
            _salesService = salesService;
            _purchasingService = purchasingService;
            _inventoryService = inventoryService;
            _securityService = securityService;
            _userManager = userManager;
        }

        [HttpGet]
        [Route("Setup")]
        public IActionResult Setup()
        {
            Api.Data.Initializer initializer = new Data.Initializer(_adminService, _financialService, _salesService, _purchasingService, _inventoryService, _securityService);
            bool success = initializer.Setup();
            if (success)
                return Ok("{ 'message': 'Initialization completed!' }");
            else
                return BadRequest("{ 'message': 'Initialization not successful! Please check the log.' }");
        }

        [HttpGet]
        [Route("clear")]
        public IActionResult Clear()
        {
            Api.Data.Initializer initializer = new Data.Initializer(_adminService, _financialService, _salesService, _purchasingService, _inventoryService, _securityService);
            bool success = initializer.Clear();
            if (success)
                return Ok("{ 'message': 'Database is cleared!' }");
            else
                return BadRequest("{ 'message': 'Clearing database not successful! Please check the log.' }");
        }

        [AllowAnonymous]
        [HttpGet("company")]
        public IActionResult Company([FromQuery] string? slug = null, [FromQuery] string? companyCode = null, [FromQuery] int? id = null)
        {
            Core.Domain.Company? company = null;

            if (id.HasValue && id.Value > 0)
            {
                company = _adminService.GetCompanies().FirstOrDefault(c => c.Id == id.Value);
            }
            else if (!string.IsNullOrWhiteSpace(slug))
            {
                company = _adminService.GetCompanyBySlug(slug);
            }
            else if (!string.IsNullOrWhiteSpace(companyCode))
            {
                company = _adminService.GetCompanies().FirstOrDefault(c => string.Equals(c.CompanyCode, companyCode, StringComparison.OrdinalIgnoreCase));
            }

            company ??= _adminService.GetDefaultCompany();

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
                plan = company.Plan ?? "accounting",
                subscriptionStatus = company.SubscriptionStatus ?? "active",
                subscriptionValidUntil = company.SubscriptionValidUntil,
                activeModules = company.GetActiveModules()
            });
        }

        [HttpGet("financialyears")]
        public IActionResult FinancialYears()
        {
            var years = _adminService.GetFinancialYears();
            return Ok(years.Select(f => new
            {
                id = f.Id,
                fiscalYearCode = f.FiscalYearCode,
                fiscalYearName = f.FiscalYearName,
                startDate = f.StartDate,
                endDate = f.EndDate,
                isActive = f.IsActive
            }));
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

        [AllowAnonymous]
        [HttpGet("companies")]
        public IActionResult Companies()
        {
            var companies = _adminService.GetCompanies();
            return Ok(companies.Select(c => new
            {
                id = c.Id.ToString(),
                name = c.Name,
                shortName = c.ShortName,
                slug = string.IsNullOrWhiteSpace(c.Slug) ? (c.ShortName?.ToLowerInvariant() ?? $"org-{c.Id}") : c.Slug,
                plan = c.Plan ?? "accounting",
                subscriptionStatus = c.SubscriptionStatus ?? "active",
                activeModules = c.GetActiveModules(),
                companyCode = c.CompanyCode,
                cra = c.CRA
            }));
        }

        public class CreateCompanyRequest
        {
            public string Name { get; set; } = string.Empty;
            public string? ShortName { get; set; }
            public string? Slug { get; set; }
            public string? Plan { get; set; } = "invoicing";
        }

        [AllowAnonymous]
        [HttpPost("company")]
        public IActionResult CreateCompany([FromBody] CreateCompanyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
                return BadRequest(new { message = "Company name is required." });

            var slug = !string.IsNullOrWhiteSpace(request.Slug)
                ? request.Slug.ToLowerInvariant().Trim()
                : request.Name.ToLowerInvariant().Replace(" ", "-").Trim();

            var shortName = !string.IsNullOrWhiteSpace(request.ShortName)
                ? request.ShortName
                : (request.Name.Length <= 5 ? request.Name.ToUpper() : request.Name.Substring(0, 4).ToUpper());

            var company = new Core.Domain.Company
            {
                Name = request.Name,
                ShortName = shortName,
                Slug = slug,
                CompanyCode = new Random().Next(100, 999).ToString(),
                Plan = request.Plan?.ToLowerInvariant() ?? "invoicing",
                SubscriptionStatus = "active"
            };

            _adminService.SaveCompany(company);

            return Ok(new
            {
                id = company.Id.ToString(),
                name = company.Name,
                shortName = company.ShortName,
                slug = company.Slug,
                plan = company.Plan,
                subscriptionStatus = company.SubscriptionStatus,
                activeModules = company.GetActiveModules()
            });
        }

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

        [HttpGet]
        [Route("users")] // api/Administration/Users
        public IActionResult Users()
        {
            var users = _securityService.GetAllUser();
            var usersDto = new List<User>();

            foreach (var user in users)
            {
                usersDto.Add(MapToUserDto(user));
            }

            return Ok(usersDto);
        }

        public class CreateMemberRequest
        {
            public string Email { get; set; } = string.Empty;
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public List<int>? RoleIds { get; set; }
        }

        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] CreateMemberRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Email))
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid Email",
                    Detail = "Email address is required.",
                    Status = StatusCodes.Status400BadRequest
                });

            var email = request.Email.Trim().ToLowerInvariant();

            // Check if user already exists in domain model
            var existingDomain = _securityService.GetUser(email);
            if (existingDomain != null)
                return BadRequest(new ProblemDetails
                {
                    Title = "User Already Exists",
                    Detail = $"A member with email '{email}' already exists in this organization.",
                    Status = StatusCodes.Status400BadRequest
                });

            // Ensure ApplicationUser in Identity
            var identityUser = await _userManager.FindByEmailAsync(email);
            if (identityUser == null)
            {
                identityUser = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };
                var createResult = await _userManager.CreateAsync(identityUser);
                if (!createResult.Succeeded)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Failed to Create User",
                        Detail = string.Join("; ", createResult.Errors.Select(e => e.Description)),
                        Status = StatusCodes.Status400BadRequest
                    });
                }
            }

            var roleIds = request.RoleIds ?? new List<int>();
            _securityService.AddUser(email, email, request.FirstName?.Trim() ?? string.Empty, request.LastName?.Trim() ?? string.Empty, roleIds);

            var createdUser = _securityService.GetUser(email);
            return Ok(createdUser != null ? MapToUserDto(createdUser) : null);
        }

        public class UpdateMemberRequest
        {
            public string? FirstName { get; set; }
            public string? LastName { get; set; }
            public string? Email { get; set; }
            public List<int>? RoleIds { get; set; }
        }

        [HttpPut("users/{id:int}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateMemberRequest request)
        {
            var user = _securityService.GetUserById(id);
            if (user == null)
                return NotFound(new ProblemDetails
                {
                    Title = "User Not Found",
                    Detail = $"Member with ID {id} was not found.",
                    Status = StatusCodes.Status404NotFound
                });

            if (request.FirstName != null)
                user.Firstname = request.FirstName.Trim();

            if (request.LastName != null)
                user.Lastname = request.LastName.Trim();

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var newEmail = request.Email.Trim().ToLowerInvariant();
                if (!string.Equals(user.EmailAddress, newEmail, StringComparison.OrdinalIgnoreCase))
                {
                    var existing = _securityService.GetUser(newEmail);
                    if (existing != null && existing.Id != id)
                        return BadRequest(new ProblemDetails
                        {
                            Title = "Email In Use",
                            Detail = $"Email '{newEmail}' is already assigned to another user.",
                            Status = StatusCodes.Status400BadRequest
                        });

                    var oldEmail = user.EmailAddress;
                    user.EmailAddress = newEmail;
                    user.UserName = newEmail;

                    var identityUser = await _userManager.FindByEmailAsync(oldEmail);
                    if (identityUser != null)
                    {
                        identityUser.Email = newEmail;
                        identityUser.UserName = newEmail;
                        await _userManager.UpdateAsync(identityUser);
                    }
                }
            }

            _securityService.UpdateUser(user, request.RoleIds);

            var updatedUser = _securityService.GetUserById(id);
            return Ok(MapToUserDto(updatedUser));
        }

        [HttpDelete("users/{id:int}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = _securityService.GetUserById(id);
            if (user == null)
                return NotFound(new ProblemDetails
                {
                    Title = "User Not Found",
                    Detail = $"Member with ID {id} was not found.",
                    Status = StatusCodes.Status404NotFound
                });

            // Prevent deleting own account
            var currentCallerEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst(ClaimTypes.Name)?.Value;
            if (!string.IsNullOrEmpty(currentCallerEmail) && string.Equals(user.EmailAddress, currentCallerEmail, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Action Denied",
                    Detail = "You cannot delete your own account.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Prevent deleting last sysadmin
            if (user.IsSysAdmin())
            {
                var allUsers = _securityService.GetAllUser();
                var adminCount = allUsers.Count(u => u.IsSysAdmin());
                if (adminCount <= 1)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Action Denied",
                        Detail = "Cannot delete the last System Administrator in the organization.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }
            }

            var email = user.EmailAddress;
            _securityService.DeleteUser(id);

            var identityUser = await _userManager.FindByEmailAsync(email);
            if (identityUser != null)
            {
                await _userManager.DeleteAsync(identityUser);
            }

            return Ok(new { success = true, message = $"User '{email}' was removed successfully." });
        }

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

        [HttpPost("roles")]
        public IActionResult SaveRole([FromBody] SaveRoleRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Name))
                return BadRequest(new ProblemDetails { Title = "Invalid Role", Detail = "Role name is required.", Status = StatusCodes.Status400BadRequest });

            var displayName = !string.IsNullOrWhiteSpace(request.DisplayName) ? request.DisplayName.Trim() : request.Name.Trim();
            _securityService.SaveRoleWithPermissions(request.Id, request.Name.Trim(), displayName, request.PermissionIds ?? new List<int>());

            return Ok(new { success = true });
        }

        [HttpDelete("roles/{id:int}")]
        public IActionResult DeleteRole(int id)
        {
            var role = _securityService.GetSecurityRole(id);
            if (role == null)
                return NotFound(new ProblemDetails { Title = "Role Not Found", Detail = $"Role with ID {id} was not found.", Status = StatusCodes.Status404NotFound });

            if (role.SysAdmin || role.Name == "SystemAdministrators" || role.Name == "GeneralUsers")
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Action Denied",
                    Detail = "System-critical roles cannot be deleted.",
                    Status = StatusCodes.Status400BadRequest
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

        [HttpPost("SaveCompany")]
        [HttpPut("company")]
        public IActionResult SaveCompany([FromBody] UpdateCompanyRequest request)
        {
            if (request == null)
                return BadRequest(new { message = "Invalid company update request." });

            Core.Domain.Company? company = null;
            if (request.Id.HasValue && request.Id.Value > 0)
            {
                company = _adminService.GetCompanies().FirstOrDefault(c => c.Id == request.Id.Value);
            }
            else if (!string.IsNullOrWhiteSpace(request.Slug))
            {
                company = _adminService.GetCompanyBySlug(request.Slug);
            }

            company ??= _adminService.GetDefaultCompany();
            if (company == null)
            {
                company = new Core.Domain.Company();
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
                plan = company.Plan,
                subscriptionStatus = company.SubscriptionStatus,
                activeModules = company.GetActiveModules()
            });
        }
    }
}
