//-----------------------------------------------------------------------
// <copyright file="BaseService.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Services
{
    public abstract class BaseService
    {
        private readonly IRepository<SequenceNumber> _sequenceNumberRepo;
        private readonly IRepository<GeneralLedgerSetting> _generalLedgerSettingRepo;
        private readonly IRepository<PaymentTerm> _paymentTermRepo;
        private readonly IRepository<Bank> _bankRepo;

        internal BaseService(IRepository<SequenceNumber> sequenceNumberRepo,
            IRepository<GeneralLedgerSetting> generalLedgerSettingRepo,
            IRepository<PaymentTerm> paymentTermRepo,
            IRepository<Bank> bankRepo)
        {
            _sequenceNumberRepo = sequenceNumberRepo;
            _generalLedgerSettingRepo = generalLedgerSettingRepo;
            _paymentTermRepo = paymentTermRepo;
            _bankRepo = bankRepo;
        }

        protected int GetNextNumber(SequenceNumberTypes type)
        {
            int nextNumber = 1;
            var sequence = (from n in _sequenceNumberRepo.Table
                            where n.SequenceNumberType == type
                            select n).FirstOrDefault();
            if (sequence == null)
            {
                sequence = new SequenceNumber();
                sequence.Description = Enum.GetName(typeof(SequenceNumberTypes), type);
                sequence.UsePrefix = false;
                sequence.SequenceNumberType = type;
                sequence.NextNumber = nextNumber + 1;
                _sequenceNumberRepo.Insert(sequence);
            }
            else
            {
                nextNumber = sequence.NextNumber;
                sequence.NextNumber += 1;
                _sequenceNumberRepo.Update(sequence);
            }
            return nextNumber;
        }

        protected GeneralLedgerSetting GetGeneralLedgerSetting()
        {
            var query = from f in _generalLedgerSettingRepo.Table
                        select f;
            return query.FirstOrDefault();
        }

        //protected IEnumerable<PaymentTerm> GetPaymentTerms()
        //{
        //    var query = from f in _paymentTermRepo.Table
        //                select f;
        //    return query;
        //}

        protected IEnumerable<Bank> GetCashAndBanks()
        {
            var query = from b in _bankRepo.Table
                        select b;
            return query;
        }
    }
}
