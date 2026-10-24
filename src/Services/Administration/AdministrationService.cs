//-----------------------------------------------------------------------
// <copyright file="AdministrationService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using AutoMapper;
using Core.Data;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Error;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Dto.Subscription;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Services.Administration
{
    public partial class AdministrationService : BaseService, IAdministrationService
    {
        private readonly IRepository<FinancialYear> _fiscalYearRepo;
        private readonly IRepository<TaxGroup> _taxGroupRepo;
        private readonly IRepository<ItemTaxGroup> _itemTaxGroupRepo;
        private readonly IRepository<PaymentTerm> _paymentTermRepo;
        private readonly IRepository<Bank> _bankRepo;
        private readonly IRepository<GeneralLedgerSetting> _genetalLedgerSetting;
        private readonly IRepository<Tax> _taxRepo;
        private readonly IRepository<Company> _company;
        private readonly IRepository<Account> _accountRepo;
        private readonly IRepository<AuditLog> _auditLogRepo;
        private readonly IRepository<SalesInvoiceLine> _salesInvoiceLineRepo;
        private readonly IRepository<PurchaseInvoiceLine> _purchaseInvoiceLineRepo;
        private readonly ISecurityRepository _securityRepository;
        private readonly IMapper _mapper;
        private readonly Core.Tenancy.ITenantContext? _tenantContext;

        public AdministrationService(IRepository<FinancialYear> fiscalYearRepo,
            IRepository<TaxGroup> taxGroupRepo,
            IRepository<ItemTaxGroup> itemTaxGroupRepo,
            IRepository<PaymentTerm> paymentTermRepo,
            IRepository<Bank> bankRepo,
            IRepository<Tax> taxRepo,
            IRepository<GeneralLedgerSetting> generalLedgerSetting,
            IRepository<Account> accountRepo,
            IRepository<AuditLog> auditLogRepo,
            IRepository<SalesInvoiceLine> salesInvoiceLineRepo,
            IRepository<PurchaseInvoiceLine> purchaseInvoiceLineRepo,
            ISecurityRepository securityRepository,
            IMapper mapper,
            IRepository<Company> company = null,
            Core.Tenancy.ITenantContext? tenantContext = null
            )
            : base(null, generalLedgerSetting, paymentTermRepo, bankRepo)
        {
            _fiscalYearRepo = fiscalYearRepo;
            _taxGroupRepo = taxGroupRepo;
            _itemTaxGroupRepo = itemTaxGroupRepo;
            _paymentTermRepo = paymentTermRepo;
            _bankRepo = bankRepo;
            _genetalLedgerSetting = generalLedgerSetting;
            _taxRepo = taxRepo;
            _company = company;
            _accountRepo = accountRepo;
            _auditLogRepo = auditLogRepo;
            _salesInvoiceLineRepo = salesInvoiceLineRepo;
            _purchaseInvoiceLineRepo = purchaseInvoiceLineRepo;
            _securityRepository = securityRepository;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public ICollection<Tax> GetAllTaxes(bool includeInActive)
        {
            var query = from f in _taxRepo.Table
                        select f;
            return query.ToList();
        }

        public void AddNewTax(Tax tax)
        {
            _taxRepo.Insert(tax);
        }

        public TaxGroup AddNewTaxGroup(Dto.TaxSystem.TaxGroup taxGroupDto)
        {
            var taxGroupEntity = _taxGroupRepo.GetById(taxGroupDto.Id);
            if (taxGroupEntity is null)
            {
                taxGroupEntity = _mapper.Map<Core.Domain.TaxSystem.TaxGroup>(taxGroupDto);
                _taxGroupRepo.Insert(taxGroupEntity);
            }

            return taxGroupEntity;
        }

        public ItemTaxGroup AddNewItemTaxGroup(Dto.TaxSystem.ItemTaxGroup itemTaxGroupDto)
        {
            var itemTaxGroupEntity = _itemTaxGroupRepo.GetById(itemTaxGroupDto.Id);
            if (itemTaxGroupEntity is null)
            {
                itemTaxGroupEntity = _mapper.Map<Core.Domain.TaxSystem.ItemTaxGroup>(itemTaxGroupDto);
                _itemTaxGroupRepo.Insert(itemTaxGroupEntity);
            }

            return itemTaxGroupEntity;
        }

        public async Task<Result<Dto.TaxSystem.TaxGroup>> DeleteTaxGroupAsync(int taxGroupId)
        {
            var taxGroup = _taxGroupRepo.GetById(taxGroupId);

            if (taxGroup is null)
            {
                var message = $"TaxGroup with id {taxGroupId} not found";
                return Result<Dto.TaxSystem.TaxGroup>.Failure(Error.RecordNotFound(message));
            }

            await _taxGroupRepo.DeleteAsync(taxGroup);

            return Result<Dto.TaxSystem.TaxGroup>.Success(null);
        }

        public async Task<Result<Dto.TaxSystem.ItemTaxGroup>> DeleteItemTaxGroupAsync(int itemTaxGroupId)
        {
            var itemTaxGroup = _itemTaxGroupRepo.GetById(itemTaxGroupId);

            if (itemTaxGroup is null)
            {
                var message = $"ItemTaxGroup with id {itemTaxGroupId} not found";
                return Result<Dto.TaxSystem.ItemTaxGroup>.Failure(Error.RecordNotFound(message));
            }

            await _itemTaxGroupRepo.DeleteAsync(itemTaxGroup);

            return Result<Dto.TaxSystem.ItemTaxGroup>.Success(null);
        }

        public ICollection<ItemTaxGroup> GetItemTaxGroups(bool includeInActive)
        {
            var query = from f in _itemTaxGroupRepo.Table
                        select f;
            return query.ToList();
        }

        public ICollection<TaxGroup> GetTaxGroups(bool includeInActive)
        {
            var query = from f in _taxGroupRepo.Table
                        select f;
            return query.ToList();
        }

        public void InitializeCompany()
        {
            if (_company.Table.FirstOrDefault() == null)
            {
                //DbInitializerHelper.Initialize();
            }
        }

        public ICollection<Company> GetCompanies()
        {
            return _company.Table.ToList();
        }

        public Company? GetCompanyBySlug(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug)) return null;
            return _company.Table.FirstOrDefault(c => c.Slug == slug);
        }

        public ICollection<PaymentTerm> GetPaymentTerms()
        {
            var query = from f in _paymentTermRepo.Table
                        select f;
            return query.ToList();
        }

        public ICollection<FinancialYear> GetFinancialYears()
        {
            var query = from f in _fiscalYearRepo.Table
                        select f;
            return query.ToList();
        }

        public void SaveCompany(Company company)
        {
            if (company.Id == 0)
            {
                _company.Insert(company);
            }
            else
            {
                if (_tenantContext?.CompanyId.HasValue == true && company.Id != _tenantContext.CompanyId.Value)
                {
                    throw new Core.Tenancy.CrossTenantWriteException("Cannot update another organization's settings.");
                }
                _company.Update(company);
            }
        }

        public bool IsSystemInitialized()
        {
            bool initialized = true;

            if (_company.Table.FirstOrDefault() == null)
                return false;

            if (_accountRepo.Table.AsEnumerable().Count() < 6)
                return false;

            return initialized;
        }

        public void SaveUser(User user)
        {
            var standardRole = _securityRepository.GetAllRoles().Where(r => r.Name == "GeneralUsers").FirstOrDefault();
            var securityUserRole = new SecurityUserRole();
            securityUserRole.SecurityRole = standardRole;
            securityUserRole.User = user;
            user.Roles.Add(securityUserRole);
            _securityRepository.AddUser(user);
        }

        public User GetUser(string username)
        {
            return _securityRepository.GetUser(username);
        }

        public IEnumerable<AuditLog> AuditLogs()
        {
            return _auditLogRepo.Table.AsEnumerable();
        }

        public TenantSubscriptionDto GetCurrentSubscription()
        {
            // Workspace context only. Never accept a client-supplied slug/id here.
            int companyId = _tenantContext?.RequireCompanyId()
                ?? throw new Core.Tenancy.TenantNotResolvedException();
            var company = _company.GetById(companyId)
                ?? throw new Core.Tenancy.TenantEntityNotFoundException(nameof(Company), companyId);

            return new TenantSubscriptionDto
            {
                CompanyId = company.Id,
                CompanyName = company.Name ?? "AccountGo Organization",
                Slug = company.Slug ?? $"org-{company.Id}",
                Plan = company.Plan ?? "accounting",
                SubscriptionStatus = company.SubscriptionStatus ?? "active",
                SubscriptionValidUntil = company.SubscriptionValidUntil,
                ActiveModules = company.GetActiveModules()
            };
        }

        public TenantSubscriptionDto UpdateSubscription(UpdateSubscriptionDto dto)
        {
            // Workspace context only: dto.CompanyId / dto.Slug are ignored.
            int companyId = _tenantContext?.RequireCompanyId()
                ?? throw new Core.Tenancy.TenantNotResolvedException();

            var company = _company.GetById(companyId);
            if (company == null)
            {
                throw new Core.Tenancy.TenantEntityNotFoundException(nameof(Company), companyId);
            }

            company.Plan = dto.Plan?.ToLowerInvariant() ?? "invoicing";
            var planModules = Company.ModulesForPlan(company.Plan);
            if (dto.ActiveModules != null && dto.ActiveModules.Count > 0)
            {
                var requested = dto.ActiveModules
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m.Trim().ToLowerInvariant())
                    .Distinct()
                    .ToList();
                var disallowed = requested.Where(m => !planModules.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
                if (disallowed.Count > 0)
                    throw new ArgumentException(
                        $"Module(s) not included in the {company.Plan} plan: {string.Join(", ", disallowed)}.");

                // Keep plan order; sales is always required for every tier.
                var sanitized = planModules.Where(m =>
                    string.Equals(m, "sales", StringComparison.OrdinalIgnoreCase)
                    || requested.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
                if (sanitized.Count == 0)
                    sanitized = new List<string> { "sales" };
                company.ActiveModulesJson = System.Text.Json.JsonSerializer.Serialize(sanitized);
            }
            else
            {
                company.ActiveModulesJson = null; // Revert to plan defaults
            }

            _company.Update(company);
            return GetCurrentSubscription();
        }

        public bool IsModuleEnabled(string moduleKey)
        {
            if (_tenantContext == null || !_tenantContext.CompanyId.HasValue)
                return false;

            var company = _company.GetById(_tenantContext.CompanyId.Value);
            if (company == null) return false;
            return company.HasModule(moduleKey);
        }

        public List<PlanDefinitionDto> GetPlanDefinitions()
        {
            return new List<PlanDefinitionDto>
            {
                new PlanDefinitionDto
                {
                    Key = "invoicing",
                    Name = "Invoicing",
                    Description = "Everything you need to create quotes, bill clients, and track payments.",
                    IncludedModules = Company.ModulesForPlan("invoicing"),
                    MonthlyPrice = 15m
                },
                new PlanDefinitionDto
                {
                    Key = "accounting",
                    Name = "Accounting",
                    Description = "Full double-entry bookkeeping, banking, purchase bills, and financial statements.",
                    IncludedModules = Company.ModulesForPlan("accounting"),
                    MonthlyPrice = 39m
                },
                new PlanDefinitionDto
                {
                    Key = "enterprise",
                    Name = "Enterprise",
                    Description = "Complete ERP suite with inventory management, non-profit donations, audit trails, and whitelabeling.",
                    IncludedModules = Company.ModulesForPlan("enterprise"),
                    MonthlyPrice = 99m
                }
            };
        }

        public List<ModuleDefinitionDto> GetModuleDefinitions()
        {
            // Ordered by subscription tier: Invoicing → Accounting → Enterprise.
            return new List<ModuleDefinitionDto>
            {
                new ModuleDefinitionDto
                {
                    Key = "sales",
                    Name = "Invoicing & Sales",
                    Description = "Customers, quotations, sales orders, invoices, and payments.",
                    Icon = "Receipt",
                    IsCore = true
                },
                new ModuleDefinitionDto
                {
                    Key = "financials",
                    Name = "General Ledger & Banking",
                    Description = "Chart of accounts, cash & bank accounts, journal entries, and trial balance.",
                    Icon = "BookOpen",
                    IsCore = false
                },
                new ModuleDefinitionDto
                {
                    Key = "purchases",
                    Name = "Purchases & Bills",
                    Description = "Vendors, purchase orders, vendor invoices, and bill payments.",
                    Icon = "ShoppingBag",
                    IsCore = false
                },
                new ModuleDefinitionDto
                {
                    Key = "reports",
                    Name = "Financial Reports",
                    Description = "Income statements, balance sheets, customer/vendor aging, and tax summaries.",
                    Icon = "BarChart3",
                    IsCore = false
                },
                new ModuleDefinitionDto
                {
                    Key = "inventory",
                    Name = "Inventory & Items",
                    Description = "Product catalog, warehouse stock items, and inventory control journals.",
                    Icon = "Boxes",
                    IsCore = false
                },
                new ModuleDefinitionDto
                {
                    Key = "donations",
                    Name = "Donations & Non-Profit",
                    Description = "Donor tracking, donation invoices, pledges, and tax receipts.",
                    Icon = "HeartHandshake",
                    IsCore = false
                },
                new ModuleDefinitionDto
                {
                    Key = "audit",
                    Name = "Audit Trail & Compliance",
                    Description = "Comprehensive system audit logging and user activity monitoring.",
                    Icon = "ShieldCheck",
                    IsCore = false
                }
            };
        }
    }
}
