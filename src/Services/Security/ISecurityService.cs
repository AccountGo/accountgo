//-----------------------------------------------------------------------
// <copyright file="ISecurityService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Security;
using System.Collections.Generic;

namespace Services.Security
{
    public interface ISecurityService
    {
        IEnumerable<SecurityUserRole> GetRolesForUser(string username);
        IEnumerable<SecurityRolePermission> GetPermissionsForRole(string rolename);
        IEnumerable<string> GetPermissionsForUser(string username);
        User GetUser(string username);
        User GetUserById(int id);
        SecurityRole GetRole(string rolename);
        SecurityRole GetSecurityRole(int securityRoleId);
        SecurityPermission GetSecurityPermission(int securityPermissionId);
        bool CheckPermission(string permissionName, string username);
        IEnumerable<SecurityUserRole> GetUsersInSecurityRole(string seurityRoleName);
        void AddUserInRole(int userId, int securityRoleId);
        void RemoveUserInRole(int userId, int roleId);
        void AddPermissionToRole(int roleId, int permissionId);
        IEnumerable<SecurityRole> GetAllSecurityRole();
        IEnumerable<SecurityGroup> GetAllSecurityGroup();
        List<int> GetPermissionByRoleId(int securityRoleId);
        //void RemoveRolePermission(int roleId);
        IEnumerable<User> GetAllUser();
        void AddRole(string roleName, int roleId = 0);
        void DeleteRole(int roleId);
        void SaveRoleWithPermissions(int roleId, string name, string displayName, IEnumerable<int> permissionIds);
        void AddUser(string username, string email, string firstname, string lastname);
        void AddUser(string username, string email, string firstname, string lastname, IEnumerable<int> roleIds);
        void UpdateUser(User user, IEnumerable<int> roleIds);
        void DeleteUser(int id);
        void EnsureDefaultPermissionsAndRoles();
    }
}
