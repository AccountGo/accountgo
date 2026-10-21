using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain.Security;
using Moq;
using Services.Security;
using Xunit;

namespace AccountGo.ServicesTests;

public class SecurityServiceTests
{
    private readonly Mock<ISecurityRepository> _mockSecurityRepo;
    private readonly Mock<IRepository<User>> _mockUserRepo;
    private readonly Mock<IRepository<SecurityRole>> _mockSecurityRoleRepo;
    private readonly Mock<IRepository<SecurityPermission>> _mockSecurityPermissionRepo;
    private readonly Mock<IRepository<SecurityGroup>> _mockSecurityGroupRepo;
    private readonly Mock<IRepository<SecurityUserRole>> _mockSecurityUserRoleRepo;
    private readonly Mock<IRepository<SecurityRolePermission>> _mockSecurityRolePermissionRepo;

    private readonly List<User> _users = new();
    private readonly List<SecurityRole> _roles = new();
    private readonly List<SecurityPermission> _permissions = new();
    private readonly List<SecurityGroup> _groups = new();
    private readonly List<SecurityUserRole> _userRoles = new();
    private readonly List<SecurityRolePermission> _rolePermissions = new();

    private readonly SecurityService _service;

    public SecurityServiceTests()
    {
        _mockSecurityRepo = new Mock<ISecurityRepository>();
        _mockUserRepo = new Mock<IRepository<User>>();
        _mockSecurityRoleRepo = new Mock<IRepository<SecurityRole>>();
        _mockSecurityPermissionRepo = new Mock<IRepository<SecurityPermission>>();
        _mockSecurityGroupRepo = new Mock<IRepository<SecurityGroup>>();
        _mockSecurityUserRoleRepo = new Mock<IRepository<SecurityUserRole>>();
        _mockSecurityRolePermissionRepo = new Mock<IRepository<SecurityRolePermission>>();

        _mockUserRepo.Setup(r => r.Table).Returns(() => _users.AsQueryable());
        _mockUserRepo.Setup(r => r.Insert(It.IsAny<User>())).Callback<User>(u =>
        {
            u.Id = _users.Count + 1;
            _users.Add(u);
        });

        _mockSecurityRoleRepo.Setup(r => r.Table).Returns(() => _roles.AsQueryable());
        _mockSecurityRoleRepo.Setup(r => r.Insert(It.IsAny<SecurityRole>())).Callback<SecurityRole>(r =>
        {
            r.Id = _roles.Count + 1;
            _roles.Add(r);
        });

        _mockSecurityPermissionRepo.Setup(r => r.Table).Returns(() => _permissions.AsQueryable());
        _mockSecurityPermissionRepo.Setup(r => r.Insert(It.IsAny<SecurityPermission>())).Callback<SecurityPermission>(p =>
        {
            p.Id = _permissions.Count + 1;
            _permissions.Add(p);
        });

        _mockSecurityGroupRepo.Setup(r => r.Table).Returns(() => _groups.AsQueryable());
        _mockSecurityGroupRepo.Setup(r => r.Insert(It.IsAny<SecurityGroup>())).Callback<SecurityGroup>(g =>
        {
            g.Id = _groups.Count + 1;
            _groups.Add(g);
        });

        _mockSecurityUserRoleRepo.Setup(r => r.Table).Returns(() => _userRoles.AsQueryable());
        _mockSecurityUserRoleRepo.Setup(r => r.Insert(It.IsAny<SecurityUserRole>())).Callback<SecurityUserRole>(ur =>
        {
            ur.Id = _userRoles.Count + 1;
            _userRoles.Add(ur);
        });
        _mockSecurityUserRoleRepo.Setup(r => r.Delete(It.IsAny<SecurityUserRole>())).Callback<SecurityUserRole>(ur =>
        {
            _userRoles.Remove(ur);
        });

        _mockSecurityRolePermissionRepo.Setup(r => r.Table).Returns(() => _rolePermissions.AsQueryable());
        _mockSecurityRolePermissionRepo.Setup(r => r.Insert(It.IsAny<SecurityRolePermission>())).Callback<SecurityRolePermission>(rp =>
        {
            rp.Id = _rolePermissions.Count + 1;
            _rolePermissions.Add(rp);
        });

        _mockSecurityRepo.Setup(r => r.GetUser(It.IsAny<string>()))
            .Returns<string>(u => _users.FirstOrDefault(x => x.UserName == u));
        _mockSecurityRepo.Setup(r => r.GetUserById(It.IsAny<int>()))
            .Returns<int>(id => _users.FirstOrDefault(x => x.Id == id));
        _mockSecurityRepo.Setup(r => r.GetAllUsers())
            .Returns(() => _users);
        _mockSecurityRepo.Setup(r => r.AddUser(It.IsAny<User>())).Callback<User>(u =>
        {
            if (u.Id == 0)
            {
                u.Id = _users.Count + 1;
                _users.Add(u);
            }
        });
        _mockSecurityRepo.Setup(r => r.DeleteUser(It.IsAny<int>())).Callback<int>(id =>
        {
            var u = _users.FirstOrDefault(x => x.Id == id);
            if (u != null) _users.Remove(u);
        });

        _service = new SecurityService(
            _mockSecurityRepo.Object,
            _mockUserRepo.Object,
            _mockSecurityRoleRepo.Object,
            _mockSecurityPermissionRepo.Object,
            _mockSecurityGroupRepo.Object,
            _mockSecurityUserRoleRepo.Object,
            _mockSecurityRolePermissionRepo.Object);
    }

    [Fact]
    public void EnsureDefaultPermissionsAndRoles_ShouldSeedStandardRolesAndPermissions()
    {
        // Act
        _service.EnsureDefaultPermissionsAndRoles();

        // Assert
        Assert.NotEmpty(_groups);
        Assert.NotEmpty(_permissions);
        Assert.NotEmpty(_roles);

        var adminRole = _roles.FirstOrDefault(r => r.Name == "SystemAdministrators");
        Assert.NotNull(adminRole);
        Assert.True(adminRole.SysAdmin);

        var accountantRole = _roles.FirstOrDefault(r => r.Name == "Accountant");
        Assert.NotNull(accountantRole);
        Assert.False(accountantRole.SysAdmin);

        var generalRole = _roles.FirstOrDefault(r => r.Name == "GeneralUsers");
        Assert.NotNull(generalRole);
    }

    [Fact]
    public void AddUser_WithRoleIds_ShouldCreateUserAndAssignRoles()
    {
        // Arrange
        _service.EnsureDefaultPermissionsAndRoles();
        var accountantRole = _roles.First(r => r.Name == "Accountant");

        // Act
        _service.AddUser("jane@accountgo.ph", "jane@accountgo.ph", "Jane", "Doe", new[] { accountantRole.Id });

        // Assert
        var user = _users.FirstOrDefault(u => u.EmailAddress == "jane@accountgo.ph");
        Assert.NotNull(user);
        Assert.Equal("Jane", user.Firstname);
        Assert.Equal("Doe", user.Lastname);

        var userRole = _userRoles.FirstOrDefault(ur => ur.UserId == user.Id && ur.SecurityRoleId == accountantRole.Id);
        Assert.NotNull(userRole);
    }

    [Fact]
    public void UpdateUser_ShouldModifyNameAndRoles()
    {
        // Arrange
        _service.EnsureDefaultPermissionsAndRoles();
        var accountantRole = _roles.First(r => r.Name == "Accountant");
        var adminRole = _roles.First(r => r.Name == "SystemAdministrators");

        _service.AddUser("member@accountgo.ph", "member@accountgo.ph", "Old", "Name", new[] { accountantRole.Id });
        var user = _users.First(u => u.EmailAddress == "member@accountgo.ph");

        // Act - change name and switch to admin role
        user.Firstname = "Updated";
        user.Lastname = "User";
        _service.UpdateUser(user, new[] { adminRole.Id });

        // Assert
        Assert.Equal("Updated", user.Firstname);
        Assert.DoesNotContain(_userRoles, ur => ur.UserId == user.Id && ur.SecurityRoleId == accountantRole.Id);
        Assert.Contains(_userRoles, ur => ur.UserId == user.Id && ur.SecurityRoleId == adminRole.Id);
    }

    [Fact]
    public void DeleteUser_ShouldRemoveUserFromRepository()
    {
        // Arrange
        _service.EnsureDefaultPermissionsAndRoles();
        _service.AddUser("delete.me@accountgo.ph", "delete.me@accountgo.ph", "To", "Delete");
        var user = _users.First(u => u.EmailAddress == "delete.me@accountgo.ph");

        // Act
        _service.DeleteUser(user.Id);

        // Assert
        Assert.DoesNotContain(_users, u => u.Id == user.Id);
    }
}
