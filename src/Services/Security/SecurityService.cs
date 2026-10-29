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
            if (string.IsNullOrEmpty(username)) return Enumerable.Empty<string>();
            var user = GetUser(username);

            if (user == null)
                return Enumerable.Empty<string>();

            if (user.IsSysAdmin() || (user.Roles != null && user.Roles.Any(r => r?.SecurityRole?.Name == "SystemAdministrators" || r?.SecurityRole?.SysAdmin == true)))
            {
                var permissionsSysAdmin = _securityPermissionRepo.Table.Select(p => p.Name).ToList();
                if (permissionsSysAdmin.Count == 0)
                {
                    return GetAllDefaultPermissionNames();
                }

                return permissionsSysAdmin;
            }

            var roles = GetRolesForUser(username);
            if (roles == null) return Enumerable.Empty<string>();

            var permissions = roles
                .Where(r => r?.SecurityRole?.Permissions != null)
                .SelectMany(r => r.SecurityRole.Permissions)
                .Where(p => p?.SecurityPermission != null && !string.IsNullOrEmpty(p.SecurityPermission.Name))
                .Select(p => p.SecurityPermission.Name)
                .Distinct()
                .ToList();

            return permissions;
        }

        public void EnsureDefaultPermissionsAndRoles()
        {
            var groups = new Dictionary<string, string>
            {
                ["Sales"] = "Sales & Invoicing",
                ["Financials"] = "General Ledger",
                ["Banking"] = "Banking",
                ["Purchasing"] = "Purchases & Bills",
                ["Inventory"] = "Inventory & Items",
                ["Reports"] = "Reports & Analytics",
                ["Donations"] = "Donations & Non-Profit",
                ["Audit"] = "Audit & Compliance",
                ["Admin"] = "Administration"
            };

            var groupEntities = new Dictionary<string, SecurityGroup>();
            foreach (var kvp in groups)
            {
                var group = _securityGroupRepo.Table.FirstOrDefault(g => g.Name == kvp.Key);
                if (group == null)
                {
                    group = new SecurityGroup { Name = kvp.Key, DisplayName = kvp.Value };
                    _securityGroupRepo.Insert(group);
                }
                groupEntities[kvp.Key] = group;
            }

            var permissions = new List<(string GroupKey, string Name, string DisplayName)>
            {
                ("Sales", SystemPermissions.SalesCustomersView, "View Customers"),
                ("Sales", SystemPermissions.SalesCustomersManage, "Manage Customers"),
                ("Sales", SystemPermissions.SalesInvoicesView, "View Invoices"),
                ("Sales", SystemPermissions.SalesInvoicesCreate, "Create Invoices"),
                ("Sales", SystemPermissions.SalesInvoicesPost, "Post Invoices"),
                ("Sales", SystemPermissions.SalesReceiptsView, "View Receipts"),
                ("Sales", SystemPermissions.SalesReceiptsCreate, "Create Receipts"),
                ("Sales", SystemPermissions.SalesQuotationsView, "View Quotations"),

                ("Financials", SystemPermissions.FinancialsAccountsView, "View Accounts"),
                ("Financials", SystemPermissions.FinancialsAccountsManage, "Manage Accounts"),
                ("Financials", SystemPermissions.FinancialsJournalView, "View Journal"),
                ("Financials", SystemPermissions.FinancialsJournalCreate, "Create Journal Entries"),
                ("Financials", SystemPermissions.FinancialsJournalPost, "Post Journal Entries"),

                ("Banking", SystemPermissions.BankingAccountsView, "View Bank Accounts"),
                ("Banking", SystemPermissions.BankingAccountsManage, "Manage Bank Accounts"),
                ("Banking", SystemPermissions.BankingTransactionsCreate, "Create Bank Transactions"),
                ("Banking", SystemPermissions.BankingReconcile, "Reconcile Bank Accounts"),
                ("Banking", SystemPermissions.BankingReconcileUndo, "Undo Bank Reconciliation"),

                ("Purchasing", SystemPermissions.PurchasesVendorsView, "View Vendors"),
                ("Purchasing", SystemPermissions.PurchasesVendorsManage, "Manage Vendors"),
                ("Purchasing", SystemPermissions.PurchasesOrdersView, "View Orders"),
                ("Purchasing", SystemPermissions.PurchasesBillsView, "View Bills"),
                ("Purchasing", SystemPermissions.PurchasesBillsCreate, "Create Bills"),
                ("Purchasing", SystemPermissions.PurchasesBillsPost, "Post Bills"),
                ("Purchasing", SystemPermissions.PurchasesPaymentsCreate, "Pay Bills"),

                ("Inventory", SystemPermissions.InventoryItemsView, "View Items"),
                ("Inventory", SystemPermissions.InventoryItemsManage, "Manage Items"),
                ("Inventory", SystemPermissions.InventoryAdjustmentsView, "View Adjustments"),
                ("Inventory", SystemPermissions.InventoryAdjustmentsPost, "Adjust Stock"),

                ("Reports", SystemPermissions.ReportsView, "View Reports"),

                ("Donations", SystemPermissions.DonationsView, "View Donations"),
                ("Donations", SystemPermissions.DonationsManage, "Manage Donations"),

                ("Audit", SystemPermissions.AuditLogsView, "View Audit Logs"),

                ("Admin", SystemPermissions.AdminMembersManage, "Manage Members"),
                ("Admin", SystemPermissions.AdminRolesManage, "Manage Roles"),
                ("Admin", SystemPermissions.AdminSubscriptionManage, "Manage Subscription"),
                ("Admin", SystemPermissions.AdminTaxesManage, "Manage Taxes")
            };

            foreach (var perm in permissions)
            {
                var existing = _securityPermissionRepo.Table.FirstOrDefault(p => p.Name == perm.Name);
                if (existing == null)
                {
                    var group = groupEntities[perm.GroupKey];
                    _securityPermissionRepo.Insert(new SecurityPermission
                    {
                        Name = perm.Name,
                        DisplayName = perm.DisplayName,
                        SecurityGroupId = group.Id
                    });
                }
            }

            var legacyStockPermission = _securityPermissionRepo.Table.FirstOrDefault(p => p.Name == "inventory.stock.adjust");
            if (legacyStockPermission != null)
            {
                var adjustmentPermission = _securityPermissionRepo.Table.Single(p => p.Name == SystemPermissions.InventoryAdjustmentsPost);
                foreach (var grant in _securityRolePermissionRepo.Table.Where(rp => rp.SecurityPermissionId == legacyStockPermission.Id).ToList())
                {
                    if (!_securityRolePermissionRepo.Table.Any(rp => rp.SecurityRoleId == grant.SecurityRoleId && rp.SecurityPermissionId == adjustmentPermission.Id))
                        _securityRolePermissionRepo.Insert(new SecurityRolePermission
                        {
                            SecurityRoleId = grant.SecurityRoleId,
                            SecurityPermissionId = adjustmentPermission.Id
                        });
                    _securityRolePermissionRepo.Delete(grant);
                }
                _securityPermissionRepo.Delete(legacyStockPermission);
            }

            // #413 D7: migrate financials.cashbanks.* → banking.* then drop the legacy rows.
            MigrateLegacyCashBanksPermission(
                "financials.cashbanks.view",
                new[] { SystemPermissions.BankingAccountsView });
            MigrateLegacyCashBanksPermission(
                "financials.cashbanks.manage",
                new[]
                {
                    SystemPermissions.BankingAccountsManage,
                    SystemPermissions.BankingTransactionsCreate,
                    SystemPermissions.BankingReconcile
                });


            // Ensure standard business roles with permissions
            var standardRoles = new List<(string Name, string DisplayName, bool SysAdmin, string[] Perms)>
            {
                ("SystemAdministrators", "System Administrators", true, System.Array.Empty<string>()),
                ("Accountant", "Accountant", false, new[]
                {
                    SystemPermissions.SalesCustomersView,
                    SystemPermissions.SalesCustomersManage,
                    SystemPermissions.SalesInvoicesView,
                    SystemPermissions.SalesInvoicesCreate,
                    SystemPermissions.SalesInvoicesPost,
                    SystemPermissions.SalesReceiptsView,
                    SystemPermissions.SalesReceiptsCreate,
                    SystemPermissions.FinancialsAccountsView,
                    SystemPermissions.FinancialsAccountsManage,
                    SystemPermissions.FinancialsJournalView,
                    SystemPermissions.FinancialsJournalCreate,
                    SystemPermissions.FinancialsJournalPost,
                    SystemPermissions.BankingAccountsView,
                    SystemPermissions.BankingAccountsManage,
                    SystemPermissions.BankingTransactionsCreate,
                    SystemPermissions.BankingReconcile,
                    SystemPermissions.PurchasesVendorsView,
                    SystemPermissions.PurchasesVendorsManage,
                    SystemPermissions.PurchasesBillsView,
                    SystemPermissions.PurchasesBillsCreate,
                    SystemPermissions.PurchasesBillsPost,
                    SystemPermissions.PurchasesPaymentsCreate,
                    SystemPermissions.InventoryItemsView,
                    SystemPermissions.InventoryItemsManage,
                    SystemPermissions.InventoryAdjustmentsPost,
                    SystemPermissions.ReportsView,
                    SystemPermissions.AdminTaxesManage
                }),
                ("Sales Representative", "Sales Representative", false, new[]
                {
                    SystemPermissions.SalesCustomersView,
                    SystemPermissions.SalesCustomersManage,
                    SystemPermissions.SalesQuotationsView,
                    SystemPermissions.SalesInvoicesView,
                    SystemPermissions.SalesInvoicesCreate,
                    SystemPermissions.SalesReceiptsCreate,
                    SystemPermissions.InventoryItemsView
                }),
                ("Purchasing Agent", "Purchasing Agent", false, new[]
                {
                    SystemPermissions.PurchasesVendorsView,
                    SystemPermissions.PurchasesVendorsManage,
                    SystemPermissions.PurchasesOrdersView,
                    SystemPermissions.PurchasesBillsView,
                    SystemPermissions.PurchasesBillsCreate,
                    SystemPermissions.InventoryItemsView
                }),
                ("Auditor / Viewer", "Auditor / Viewer", false, new[]
                {
                    SystemPermissions.SalesCustomersView,
                    SystemPermissions.SalesInvoicesView,
                    SystemPermissions.FinancialsAccountsView,
                    SystemPermissions.FinancialsJournalView,
                    SystemPermissions.BankingAccountsView,
                    SystemPermissions.PurchasesVendorsView,
                    SystemPermissions.PurchasesBillsView,
                    SystemPermissions.ReportsView,
                    SystemPermissions.AuditLogsView,
                    SystemPermissions.InventoryItemsView
                }),
                ("GeneralUsers", "General Users", false, new[]
                {
                    SystemPermissions.ReportsView
                })
            };

            var allDbPermissions = _securityPermissionRepo.Table.ToList();

            foreach (var rDef in standardRoles)
            {
                var role = _securityRoleRepo.Table.FirstOrDefault(r => r.Name == rDef.Name);
                if (role == null)
                {
                    role = new SecurityRole
                    {
                        Name = rDef.Name,
                        DisplayName = rDef.DisplayName,
                        SysAdmin = rDef.SysAdmin,
                        System = true
                    };
                    _securityRoleRepo.Insert(role);
                }
                else
                {
                    bool changed = false;
                    if (role.DisplayName != rDef.DisplayName) { role.DisplayName = rDef.DisplayName; changed = true; }
                    if (role.SysAdmin != rDef.SysAdmin) { role.SysAdmin = rDef.SysAdmin; changed = true; }
                    if (changed) _securityRoleRepo.Update(role);
                }

                if (rDef.Perms.Length > 0)
                {
                    var existingRolePerms = _securityRolePermissionRepo.Table.Where(rp => rp.SecurityRoleId == role.Id).ToList();
                    foreach (var pName in rDef.Perms)
                    {
                        var permEntity = allDbPermissions.FirstOrDefault(p => p.Name == pName);
                        if (permEntity != null && !existingRolePerms.Any(rp => rp.SecurityPermissionId == permEntity.Id))
                        {
                            _securityRolePermissionRepo.Insert(new SecurityRolePermission
                            {
                                SecurityRoleId = role.Id,
                                SecurityPermissionId = permEntity.Id
                            });
                        }
                    }
                }
            }
        }

        public User GetUserById(int id)
        {
            return _securityRepository.GetUserById(id);
        }

        public void DeleteUser(int id)
        {
            _securityRepository.DeleteUser(id);
        }

        public void UpdateUser(User user, IEnumerable<int> roleIds)
        {
            _securityRepository.AddUser(user);

            if (roleIds != null)
            {
                var existingRoles = _securityUserRoleRepo.Table.Where(ur => ur.UserId == user.Id).ToList();
                var desiredRoleIds = roleIds.Distinct().ToList();

                foreach (var existing in existingRoles)
                {
                    if (!desiredRoleIds.Contains(existing.SecurityRoleId))
                    {
                        _securityUserRoleRepo.Delete(existing);
                    }
                }

                foreach (var roleId in desiredRoleIds)
                {
                    if (!existingRoles.Any(r => r.SecurityRoleId == roleId))
                    {
                        _securityUserRoleRepo.Insert(new SecurityUserRole
                        {
                            UserId = user.Id,
                            SecurityRoleId = roleId
                        });
                    }
                }
            }
        }

        public void SaveRoleWithPermissions(int roleId, string name, string displayName, IEnumerable<int> permissionIds)
        {
            SecurityRole role;
            if (roleId > 0)
            {
                role = _securityRoleRepo.GetById(roleId);
                if (role != null)
                {
                    role.Name = name;
                    role.DisplayName = displayName;
                    _securityRoleRepo.Update(role);
                }
            }
            else
            {
                role = new SecurityRole
                {
                    Name = name,
                    DisplayName = displayName,
                    SysAdmin = false,
                    System = false
                };
                _securityRoleRepo.Insert(role);
            }

            if (role != null && permissionIds != null)
            {
                var existingPerms = _securityRolePermissionRepo.Table.Where(rp => rp.SecurityRoleId == role.Id).ToList();
                var desiredPermIds = permissionIds.Distinct().ToList();

                foreach (var existing in existingPerms)
                {
                    if (!desiredPermIds.Contains(existing.SecurityPermissionId))
                    {
                        _securityRolePermissionRepo.Delete(existing);
                    }
                }

                foreach (var permId in desiredPermIds)
                {
                    if (!existingPerms.Any(p => p.SecurityPermissionId == permId))
                    {
                        _securityRolePermissionRepo.Insert(new SecurityRolePermission
                        {
                            SecurityRoleId = role.Id,
                            SecurityPermissionId = permId
                        });
                    }
                }
            }
        }

        public void AddUser(string username, string email, string firstname, string lastname, IEnumerable<int> roleIds)
        {
            var user = new User()
            {
                UserName = username,
                EmailAddress = email,
                Firstname = firstname,
                Lastname = lastname
            };

            _securityRepository.AddUser(user);

            var created = _securityRepository.GetUser(username);
            if (created != null)
            {
                var roleIdList = roleIds != null && roleIds.Any()
                    ? roleIds.Distinct().ToList()
                    : new List<int>();

                foreach (var roleId in roleIdList)
                {
                    _securityUserRoleRepo.Insert(new SecurityUserRole
                    {
                        UserId = created.Id,
                        SecurityRoleId = roleId
                    });
                }
            }
        }

        private static List<string> GetAllDefaultPermissionNames()
        {
            return typeof(SystemPermissions)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy)
                .Where(fi => fi.IsLiteral && !fi.IsInitOnly && fi.FieldType == typeof(string))
                .Select(fi => (string)fi.GetValue(null)!)
                .ToList();
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

        private void MigrateLegacyCashBanksPermission(string legacyName, string[] replacementPermissionNames)
        {
            var legacy = _securityPermissionRepo.Table.FirstOrDefault(p => p.Name == legacyName);
            if (legacy == null)
                return;

            var replacements = _securityPermissionRepo.Table
                .Where(p => replacementPermissionNames.Contains(p.Name))
                .ToList();
            foreach (var grant in _securityRolePermissionRepo.Table.Where(rp => rp.SecurityPermissionId == legacy.Id).ToList())
            {
                foreach (var replacement in replacements)
                {
                    if (!_securityRolePermissionRepo.Table.Any(rp =>
                            rp.SecurityRoleId == grant.SecurityRoleId
                            && rp.SecurityPermissionId == replacement.Id))
                    {
                        _securityRolePermissionRepo.Insert(new SecurityRolePermission
                        {
                            SecurityRoleId = grant.SecurityRoleId,
                            SecurityPermissionId = replacement.Id
                        });
                    }
                }
                _securityRolePermissionRepo.Delete(grant);
            }
            _securityPermissionRepo.Delete(legacy);
        }

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
