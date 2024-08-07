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
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Services.Financial;
using Services.TaxSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

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
        private readonly IFinancialService _financialService;
        private readonly ITaxService _taxService;
        private readonly IMapper _mapper;

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
            IFinancialService financialService,
            ITaxService taxService,
            IMapper mapper,
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
            _financialService = financialService;
            _taxService = taxService;
            _mapper = mapper;
        }

        public ICollection<Tax> GetAllTaxes(bool includeInActive)
        {
            var query = from f in _taxRepo.Table
                        select f;
            return query.ToList();
        }

        public void CreateTax(Dto.TaxSystem.TaxForCreation taxForCreationDto)
        {
            var query = from f in _taxRepo.Table
                        where f.TaxCode == taxForCreationDto.TaxCode || f.TaxName == taxForCreationDto.TaxName || f.Rate == taxForCreationDto.Rate
                        select f;

            if (query.Any())
                throw new Exception("Tax already exists");

            var salesTaxAccount = _financialService.GetAccountByAccountCode(taxForCreationDto.SalesAccountId.ToString());
            var purchaseTaxAccount = _financialService.GetAccountByAccountCode(taxForCreationDto.PurchaseAccountId.ToString());

            var taxEntity = _mapper.Map<Core.Domain.TaxSystem.Tax>(taxForCreationDto);
            taxEntity.SalesAccountId = salesTaxAccount.Id;
            taxEntity.PurchasingAccountId = purchaseTaxAccount.Id;
            taxEntity.SalesAccount = salesTaxAccount;
            taxEntity.PurchasingAccount = purchaseTaxAccount;

            var taxGroupEntity = AddNewTaxGroup(taxForCreationDto.TaxGroup);
            taxEntity.TaxGroupTaxes.Add(new Core.Domain.TaxSystem.TaxGroupTax
            {
                TaxId = taxEntity.Id,
                TaxGroupId = taxGroupEntity.Id,
                TaxGroup = taxGroupEntity,
            });

            var itemTaxGroupEntity = AddNewItemTaxGroup(taxForCreationDto.ItemTaxGroup);
            taxEntity.ItemTaxGroupTaxes.Add(new Core.Domain.TaxSystem.ItemTaxGroupTax
            {
                TaxId = taxEntity.Id,
                ItemTaxGroupId = itemTaxGroupEntity.Id,
                ItemTaxGroup = itemTaxGroupEntity,
            });

            AddNewTax(taxEntity);
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

        public void EditTax(Dto.TaxSystem.TaxForUpdate taxForUpdateDto)
        {
            var salesTaxAccount = _financialService.GetAccountByAccountCode(taxForUpdateDto.SalesAccountId.ToString());
            var purchaseTaxAccount = _financialService.GetAccountByAccountCode(taxForUpdateDto.PurchaseAccountId.ToString());

            var taxEntity = _taxService.GetTaxById(taxForUpdateDto.Tax.Id);

            _mapper.Map(taxForUpdateDto, taxEntity);
            taxEntity.SalesAccountId = salesTaxAccount.Id;
            taxEntity.PurchasingAccountId = purchaseTaxAccount.Id;
            taxEntity.SalesAccount = salesTaxAccount;
            taxEntity.PurchasingAccount = purchaseTaxAccount;

            taxEntity.TaxGroupTaxes.Clear();

            var taxGroupEntity = AddNewTaxGroup(taxForUpdateDto.TaxGroup);
            taxEntity.TaxGroupTaxes.Add(new Core.Domain.TaxSystem.TaxGroupTax
            {
                TaxId = taxEntity.Id,
                TaxGroupId = taxGroupEntity.Id,
                TaxGroup = taxGroupEntity,
            });

            taxEntity.ItemTaxGroupTaxes.Clear();
            
            var itemTaxGroupEntity = AddNewItemTaxGroup(taxForUpdateDto.ItemTaxGroup);
            taxEntity.ItemTaxGroupTaxes.Add(new Core.Domain.TaxSystem.ItemTaxGroupTax
            {
                TaxId = taxEntity.Id,
                ItemTaxGroupId = itemTaxGroupEntity.Id,
                ItemTaxGroup = itemTaxGroupEntity
            });

            UpdateTax(taxEntity);
        }

        public void UpdateTax(Tax tax)
        {
            _taxRepo.Update(tax);
        }

        public void DeleteTax(int taxId)
        {
            var tax = _taxRepo.GetById(taxId);

            if(tax is null)
                throw new Exception("Tax not found");

            _taxRepo.Delete(tax);
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
            if (company.Id == 0) { 
                _company.Insert(company); 
            }
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
