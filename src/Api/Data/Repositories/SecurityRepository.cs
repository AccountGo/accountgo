using System;
using System.Linq;
using Core.Data;
using Core.Domain.Security;

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
            return _context.Users.Where(u => u.UserName == username)
                .FirstOrDefault();
        }
    }
}
