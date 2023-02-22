using Dto.Administration;
using Dto.Security;
using Microsoft.AspNetCore.Mvc;
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
using System.Text;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class AdministrationController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly IFinancialService _financialService;
        private readonly ISalesService _salesService;
        private readonly IPurchasingService _purchasingService;
        private readonly IInventoryService _inventoryService;
        private readonly ISecurityService _securityService;

        public AdministrationController(IAdministrationService adminService,
            IFinancialService financialService,
            ISalesService salesService,
            IPurchasingService purchasingService,
            IInventoryService inventoryService,
            ISecurityService securityService)
        {
            _adminService = adminService;
            _financialService = financialService;
            _salesService = salesService;
            _purchasingService = purchasingService;
            _inventoryService = inventoryService;
            _securityService = securityService;
        }

        [HttpGet]
        [Route("[action]")]
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
        [Route("[action]")]
        public IActionResult Clear()
        {
            Api.Data.Initializer initializer = new Data.Initializer(_adminService, _financialService, _salesService, _purchasingService, _inventoryService, _securityService);
            bool success = initializer.Clear();
            if (success)
                return Ok("{ 'message': 'Database is cleared!' }");
            else
                return BadRequest("{ 'message': 'Clearing database not successful! Please check the log.' }");
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Company(string companyCode)
        {
            return new ObjectResult(_adminService.GetDefaultCompany());
        }

        [HttpGet]
        [Route("[action]")]
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
        [Route("[action]")]
        public IActionResult Users()
        {
            var users = _securityService.GetAllUser();
            var usersDto = new List<User>();

            foreach (var user in users)
            {
                var userDto = new User
                {
                    Id = user.Id,
                    FirstName = user.Firstname,
                    LastName = user.Lastname,
                    Email = user.EmailAddress,
                    UserName = user.UserName
                };

                foreach (var role in user.Roles)
                {
                    var roleDto = new Role
                    {
                        Id = role.Id,
                        Name = role.SecurityRole.Name,
                        DisplayName = role.SecurityRole.DisplayName
                    };

                    userDto.Roles.Add(roleDto);
                }

                usersDto.Add(userDto);
            }

            return new ObjectResult(usersDto);
        }

        [HttpGet]
        [Route("[action]")]
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
                    DisplayName = role.DisplayName
                };

                foreach (var permission in role.Permissions)
                {
                    var permissionDto = new Permission
                    {
                        Id = permission.Id,
                        Name = permission.SecurityPermission.Name,
                        DisplayName = permission.SecurityPermission.DisplayName
                    };

                    roleDto.Permissions.Add(permissionDto);
                }

                rolesDto.Add(roleDto);
            }

            return new ObjectResult(rolesDto);
        }

        [HttpGet]
        [Route("[action]")]
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
        [Route("[action]")]
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

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveCompany([FromBody]Company companyDto)
        {
            string[] errors;
            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (var i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;
                    return new BadRequestObjectResult(errors);
                }

                Core.Domain.Company company = companyDto.Id == 0
                    ? new Core.Domain.Company()
                    : _adminService.GetDefaultCompany();

                company.CompanyCode = companyDto.CompanyCode;
                company.Name = companyDto.Name;
                company.ShortName = companyDto.ShortName;

                _adminService.SaveCompany(company);

                return new ObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new[] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }
    }
}
