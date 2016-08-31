//-----------------------------------------------------------------------
// <copyright file="AdministrationService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Collections.Generic;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using System;
using Core.Domain.TaxSystem;
using Core.Domain.Security;
using Core.Domain.Auditing;

namespace Services.Administration
{
    public class AdministrationService : BaseService, IAdministrationService
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
        private readonly ISecurityRepository _securityRepository;

        public AdministrationService(IRepository<FinancialYear> fiscalYearRepo,
            IRepository<TaxGroup> taxGroupRepo,
            IRepository<ItemTaxGroup> itemTaxGroupRepo,
            IRepository<PaymentTerm> paymentTermRepo,
            IRepository<Bank> bankRepo,
            IRepository<Tax> taxRepo,
            IRepository<GeneralLedgerSetting> generalLedgerSetting,
            IRepository<Account> accountRepo,
            IRepository<AuditLog> auditLogRepo,
            ISecurityRepository securityRepository,
            IRepository<Company> company = null
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
            _securityRepository = securityRepository;
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

        public void UpdateTax(Tax tax)
        {
            _taxRepo.Update(tax);
        }

        public void DeleteTax(int id)
        {
            throw new System.NotImplementedException();
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

        public Company GetDefaultCompany()
        {
            return _company.Table.ToList().FirstOrDefault();
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
            if (company.Id == 0) { _company.Insert(company); }
            else { _company.Update(company); }
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
    }
}
