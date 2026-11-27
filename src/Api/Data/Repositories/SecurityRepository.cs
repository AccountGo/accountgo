using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Core.Data;
using Core.Domain.Security;
using System.Collections.Generic;

namespace Api.Data.Repositories
{
    public class SecurityRepository : ISecurityRepository, IAsyncSecurityRepository
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
                .FirstOrDefault()!;
        }

        public Task<User?> GetUserAsync(string username, CancellationToken cancellationToken = default)
        {
            return _context.Users
                .AsNoTracking()
                .Include(u => u.Roles)
                .ThenInclude(u => u.SecurityRole.Permissions)
                .ThenInclude(u => u.SecurityPermission.Group)
                .Where(u => u.UserName == username)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<string>> GetAllPermissionNamesAsync(CancellationToken cancellationToken = default)
        {
            return await _context.SecurityPermissions
                .AsNoTracking()
                .Where(p => p.Name != null)
                .Select(p => p.Name!)
                .ToListAsync(cancellationToken);
        }

        public User GetUserById(int id)
        {
            return _context.Users
                .Include(u => u.Roles)
                .ThenInclude(u => u.SecurityRole.Permissions)
                .ThenInclude(u => u.SecurityPermission.Group)
                .Where(u => u.Id == id)
                .FirstOrDefault()!;
        }

        public IEnumerable<User> GetAllUsers()
        {
            var users = _context.Users.Include(u => u.Roles)
                .ThenInclude(u => u.SecurityRole.Permissions)
                .ThenInclude(u => u.SecurityPermission.Group);

            return users;
        }

        public void DeleteUser(int id)
        {
            var user = _context.Users.Include(u => u.Roles).FirstOrDefault(u => u.Id == id);
            if (user != null)
            {
                if (user.Roles != null && user.Roles.Any())
                {
                    _context.SecurityUserRoles.RemoveRange(user.Roles);
                }
                _context.Users.Remove(user);
                _context.SaveChanges();
            }
        }

        public IEnumerable<SecurityRole> GetAllRoles()
        {
            var roles = _context.SecurityRoles
                .Include(r => r.Permissions)
                .ThenInclude(rp => rp.SecurityPermission)
                .ThenInclude(sp => sp.Group)
                .Include(r => r.Users);
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
