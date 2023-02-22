using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Core.Data;
using Core.Domain.Security;
using System.Collections.Generic;

namespace Api.Data.Repositories
{
    public class SecurityRepository : ISecurityRepository
    {
        private readonly ApiDbContext _context;
        public SecurityRepository(ApiDbContext context)
        {
            _context = context;
        }

        public void AddRole(SecurityRole role)
        {
            throw new NotImplementedException();
        }

        public void AddUser(User user)
        {
            if (user.Id == 0)
                _context.Users.Add(user);
            else
                _context.Update(user);

            _context.SaveChanges();
        }

        public SecurityRole GetRole(string roleName)
        {
            throw new NotImplementedException();
        }

        public User GetUser(string username)
        {
            return _context.Users
                .Include(u => u.Roles)
                .ThenInclude(u => u.SecurityRole.Permissions)
                .ThenInclude(u => u.SecurityPermission.Group)
                .Where(u => u.UserName == username)
                .FirstOrDefault();
        }

        public IEnumerable<User> GetAllUsers()
        {
            var users = _context.Users.Include(u => u.Roles)
                .ThenInclude(u => u.SecurityRole.Permissions)
                .ThenInclude(u => u.SecurityPermission.Group);

            return users;
        }

        public IEnumerable<SecurityRole> GetAllRoles()
        {
            // Commented out the additional ThenInclude below because of the error encountered as copied below
            // An error was generated for warning 'Microsoft.EntityFrameworkCore.Query.NavigationBaseIncludeIgnored': The navigation 'SecurityRolePermission.SecurityPermission' was ignored from 'Include' in the query since the fix-up will automatically populate it. If any further navigations are specified in 'Include' afterwards then they will be ignored. Walking back include tree is not allowed. This exception can be suppressed or logged by passing event ID 'CoreEventId.NavigationBaseIncludeIgnored' to the 'ConfigureWarnings' method in 'DbContext.OnConfiguring' or 'AddDbContext'.
            var roles = _context.SecurityRoles.Include(r => r.Permissions);
                // .ThenInclude(r => r.SecurityPermission.RolePermissions)
                // .ThenInclude(r => r.SecurityPermission.Group);
            return roles;
        }

        public IEnumerable<SecurityGroup> GetAllGroups()
        {
            var groups = _context.SecurityGroups.Include(g => g.Permissions)
                .ThenInclude(g => g.RolePermissions)
                .ThenInclude(g => g.SecurityPermission);

            return groups;
        }
    }
}
