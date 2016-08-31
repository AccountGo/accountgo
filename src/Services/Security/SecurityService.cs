using Core.Data;
using Core.Domain.Security;
using System.Collections.Generic;
using System.Linq;

namespace Services.Security
{
    public class SecurityService : ISecurityService
    {
        private readonly IRepository<User> _userRepo;
        private readonly IRepository<SecurityRole> _securityRoleRepo;
        private readonly IRepository<SecurityPermission> _securityPermissionRepo;
        private readonly IRepository<SecurityGroup> _securityGroupRepo;
        private readonly IRepository<SecurityUserRole> _securityUserRoleRepo;
        private readonly IRepository<SecurityRolePermission> _securityRolePermissionRepo;
        private readonly ISecurityRepository _securityRepository;

        public SecurityService(ISecurityRepository securityRepository = null,
            IRepository<User> userRepo = null,
            IRepository<SecurityRole> securityRoleRepo = null,
            IRepository<SecurityPermission> securityPermissionRepo = null,
            IRepository<SecurityGroup> securityGroupRepo = null,
            IRepository<SecurityUserRole> securityUserRoleRepo = null,
            IRepository<SecurityRolePermission> securityRolePermissionRepo = null)
        {
            _userRepo = userRepo;
            _securityRoleRepo = securityRoleRepo;
            _securityPermissionRepo = securityPermissionRepo;
            _securityGroupRepo = securityGroupRepo;
            _securityUserRoleRepo = securityUserRoleRepo;
            _securityRolePermissionRepo = securityRolePermissionRepo;
            _securityRepository = securityRepository;
        }

        public IEnumerable<SecurityRolePermission> GetPermissionsForRole(string rolename)
        {
            var role = GetRole(rolename);

            if (role.SysAdmin)
                return _securityRolePermissionRepo.Table.AsEnumerable();

            return role.Permissions.AsEnumerable();
        }

        public IEnumerable<SecurityUserRole> GetRolesForUser(string username)
        {
            var user = GetUser(username);
            return user.Roles;
        }

        public SecurityPermission GetSecurityPermission(int securityPermissionId)
        {
            return _securityPermissionRepo.Table.Where(p => p.Id == securityPermissionId).FirstOrDefault();
        }

        public SecurityRole GetSecurityRole(int securityRoleId)
        {
            return _securityRoleRepo.Table.Where(r => r.Id == securityRoleId).FirstOrDefault();
        }

        public IEnumerable<SecurityRole> GetAllSecurityRole()
        {
            return _securityRepository.GetAllRoles();
        }

        public User GetUser(string username)
        {
            var user = _securityRepository.GetUser(username);

            return user;
        }

        public SecurityRole GetRole(string rolename)
        {
            return _securityRoleRepo.Table.Where(r => r.Name == rolename).FirstOrDefault();
        }

        public bool CheckPermission(string permissionName, string username)
        {
            bool hasPermission = false;

            var user = GetUser(username);

            foreach (var role in user.Roles)
            {
                if (role.SecurityRole.Permissions.Where(p => p.SecurityPermission != null && p.SecurityPermission.Name == permissionName).FirstOrDefault() != null)
                {
                    hasPermission = true;
                    break;
                }
            }

            return hasPermission;
        }

        public IEnumerable<SecurityUserRole> GetUsersInSecurityRole(string securityRoleName)
        {
            var role = GetRole(securityRoleName);

            return role != null ? role.Users.AsEnumerable() : null;
        }

        public void AddUserInRole(int userId, int securityRoleId)
        {
            var item = new SecurityUserRole()
            {
                UserId = userId,
                SecurityRoleId = securityRoleId
            };
            
            _securityUserRoleRepo.Insert(item);
        }

        public void RemoveUserInRole(int userId, int roleId)
        {
            var itemToDelete = _securityUserRoleRepo.Table.ToList().Where(i => i.SecurityRoleId == roleId && i.UserId == userId).FirstOrDefault();

            _securityUserRoleRepo.Delete(itemToDelete);
        }

        public void AddPermissionToRole(int roleId, int permissionId)
        {
            var securityRolePermission = new SecurityRolePermission()
            {
                SecurityPermissionId = permissionId,
                SecurityRoleId = roleId
            };

            _securityRolePermissionRepo.Insert(securityRolePermission);
        }

        public IEnumerable<string> GetPermissionsForUser(string username)
        {
            var user = GetUser(username);

            if (user == null)
                return null;

            if (user.IsSysAdmin())
            {
                var permissionsSysAdmin = from p in _securityPermissionRepo.Table.ToList()
                                  select p.Name;

                return permissionsSysAdmin;
            }

            var roles = GetRolesForUser(username);

            var permissions = roles.SelectMany(r => r.SecurityRole.Permissions).ToList().AsQueryable();

            var all = from p in permissions
                   select p.SecurityPermission.Name;

            return all;
        }

        public IEnumerable<SecurityGroup> GetAllSecurityGroup()
        {
            return _securityRepository.GetAllGroups();
        }

        public List<int> GetPermissionByRoleId(int securityRoleId)
        {
            return _securityRolePermissionRepo.Table.Where(x => x.SecurityRoleId == securityRoleId).Select(x=>x.SecurityPermissionId).ToList();
        }

        //public void RemoveRolePermission(int roleId)
        //{
        //    var rolePermission = _securityRolePermissionRepo.Table.Where(x => x.SecurityRoleId == roleId).AsEnumerable();

        //    _securityRolePermissionRepo.Delete(rolePermission);
        //}

        public IEnumerable<User> GetAllUser()
        {
            var users = _securityRepository.GetAllUsers();

            return users;
        }

        public void AddRole(string roleName, int roleId = 0)
        {
            if(roleId > 0)
            {
                var entity = _securityRoleRepo.GetById(roleId);
                entity.Name = roleName;

                _securityRoleRepo.Update(entity);
            }
            else
            {
                var role = new SecurityRole()
                {
                    Name = roleName,
                };

                _securityRoleRepo.Insert(role);
            }
        }

        public void DeleteRole(int roleId)
        {
            //RemoveRolePermission(roleId);
            var entity = _securityRoleRepo.GetById(roleId);

            _securityRoleRepo.Delete(entity);
        }

        public void AddUser(string username, string email, string firstname, string lastname)
        {
            var user = new User()
            {
                UserName = username,
                EmailAddress = email,
                Firstname = firstname,
                Lastname = lastname
            };

            _userRepo.Insert(user);
        }
    }
}
