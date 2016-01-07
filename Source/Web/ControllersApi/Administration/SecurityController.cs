using Services.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using Web.Models.ViewModels;
using Web.Models.ViewModels.Administration.Security;

namespace Web.ControllersApi.Administration
{
    public class SecurityController : ApiController
    {
        private ISecurityService _securityService;

        public SecurityController(ISecurityService securityService)
        {
            if (securityService == null)
                throw new ArgumentNullException("securityService");

            _securityService = securityService;
        }

        [HttpGet]
        [Route("api/security/GetPermissionsForUser")]
        public IHttpActionResult GetPermissionsForUser(string username)
        {
            var permissions = _securityService.GetPermissionsForUser(username);
            return Ok(permissions.AsEnumerable());
        }

        [HttpGet]
        [Route("api/security/getallsecurityroles")]
        public List<SecurityRoleModel> GetAllSecurityRoles()
        {
            var returnList = new List<SecurityRoleModel>();
            var securityRole = _securityService.GetAllSecurityRole();
            returnList = (from e in securityRole
                          select new SecurityRoleModel()
                          {
                              RoleName = e.RoleName,
                              SecurityRoleId = e.Id,
                              SysAdmin = e.SysAdmin,
                              HasUsers = e.Users.Count > 0 ? true : false
                          }).ToList();

            return returnList;
        }

        [HttpGet]
        [Route("api/security/getuser")]
        public List<SecurityUserModel> GetUser(string systemRole)
        {
            var returnList = new List<SecurityUserModel>();
            var systemUser = _securityService.GetUsersInSecurityRole(systemRole);
            returnList = (from e in systemUser
                          select new SecurityUserModel()
                          {
                              SecurityUserRoleId = e.Id,
                              UserId = e.UserId,
                              Email = e.User.Email,
                              FullName = e.User.Firstname + " " + e.User.Lastname,
                              UserName = e.User.Username
                          }).ToList();

            return returnList;
        }

        [HttpGet]
        [Route("api/security/getalluser")]
        public List<UserModel> GetAllUser(string systemRole)
        {
            var returnList = new List<UserModel>();
            var systemUser = _securityService.GetUsersInSecurityRole(systemRole);
            var existingUserId = (from e in systemUser
                                  select e.UserId);

            returnList = _securityService.GetAllUser().Where(x => !existingUserId.Contains(x.Id))
                            .Select(z => new UserModel()
                            {
                                UserId = z.Id,
                                Email = z.Email,
                                Firstname = z.Firstname,
                                Lastname = z.Lastname,
                                Username = z.Username,
                                Fullname = z.Firstname + " " + z.Lastname
                            }).ToList();

            return returnList;
        }

        [HttpGet]
        public List<SecurityGroupModel> GetAllSecurityGroups(int securityRoleId)
        {
            var returnList = new List<SecurityGroupModel>();
            var securityGroup = _securityService.GetAllSecurityGroup();
            var permission = _securityService.GetPermissionByRoleId(securityRoleId);
            var securityRole = _securityService.GetSecurityRole(securityRoleId);

            returnList = (from e in securityGroup
                          select new SecurityGroupModel()
                          {
                              Name = e.GroupName,
                              SecurityGroupId = e.Id,
                              SecurityRoleId = securityRoleId,
                              SecurityPermission = (from r in e.Permissions
                                                         let securityGroupPermission = permission.FirstOrDefault(x => x == r.Id)
                                                         select new SecurityPermissionModel()
                                                         {
                                                             Name = r.DisplayName,
                                                             SecurityPermissionId = r.Id,
                                                             IsChecked = securityGroupPermission > 0 || securityRole.SysAdmin ? true : false,
                                                         }).ToList()
                          }).ToList();

            return returnList;
        }

        [HttpPost]
        public ActionResponse SavePermission(List<SecurityGroupModel> securityGroupPermission)
        {
            var returnItem = new ActionResponse();
            returnItem.IsSuccess = true;
            returnItem.Message = "Successfully Saved!";
            try
            {
                if (securityGroupPermission != null)
                {
                    //delete all permission
                    var systemRoleId = securityGroupPermission.FirstOrDefault().SecurityRoleId;
                    //_securityService.RemoveRolePermission(systemRoleId);

                    foreach (var e in securityGroupPermission)
                    {
                        foreach (var r in e.SecurityPermission)
                        {
                            if (r.IsChecked)
                            {
                                _securityService.AddPermissionToRole(systemRoleId, r.SecurityPermissionId);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                returnItem.IsSuccess = false;
                returnItem.Message = e.Message;
            }

            return returnItem;
        }

        [HttpPost]
        public ActionResponse AddNewUser(int userId, int securityRoleId)
        {
            var returnItem = new ActionResponse();
            returnItem.IsSuccess = true;
            returnItem.Message = "Successfully Saved!";
            try
            {
                _securityService.AddUserInRole(userId, securityRoleId);
            }
            catch (Exception e)
            {
                returnItem.IsSuccess = false;
                returnItem.Message = e.Message;
            }

            return returnItem;
        }

        [HttpPost]
        public ActionResponse SaveRole(string roleName,int roleId)
        {
            var returnItem = new ActionResponse();
            returnItem.IsSuccess = true;
            returnItem.Message = "Successfully Saved!";
            try
            {
                _securityService.AddRole(roleName, roleId);
            }
            catch (Exception e)
            {
                returnItem.IsSuccess = false;
                returnItem.Message = e.Message;
            }

            return returnItem;
        }

        [HttpPost]
        public ActionResponse DeleteRole(int roleId)
        {
            var returnItem = new ActionResponse();
            returnItem.IsSuccess = true;
            returnItem.Message = "Successfully Deleted!";
            try
            {
                _securityService.DeleteRole(roleId);
            }
            catch (Exception e)
            {
                returnItem.IsSuccess = false;
                returnItem.Message = e.Message;
            }

            return returnItem;
        }

        [HttpPost]
        public ActionResponse DeleteUser(int userId, int roleId)
        {
            var returnItem = new ActionResponse();
            returnItem.IsSuccess = true;
            returnItem.Message = "Successfully Deleted!";
            try
            {
                _securityService.RemoveUserInRole(userId, roleId);
            }
            catch (Exception e)
            {
                returnItem.IsSuccess = false;
                returnItem.Message = e.Message;
            }

            return returnItem;
        }
    }
}
