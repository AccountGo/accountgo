//-----------------------------------------------------------------------
// <copyright file="BaseService.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
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

        /// <summary>The next value of a sequence without taking it; 1 when the sequence doesn't exist yet.</summary>
        protected int PeekNextNumber(SequenceNumberTypes type)
        {
            var sequence = _sequenceNumberRepo.Table.FirstOrDefault(n => n.SequenceNumberType == type);
            return Math.Max(sequence?.NextNumber ?? 1, 1);
        }

        /// <summary>Moves a sequence so its next value is <paramref name="nextNumber"/>. It only ever moves forward.</summary>
        protected void AdvanceNextNumber(SequenceNumberTypes type, int nextNumber)
        {
            var sequence = _sequenceNumberRepo.Table.FirstOrDefault(n => n.SequenceNumberType == type);
            if (sequence == null)
            {
                _sequenceNumberRepo.Insert(new SequenceNumber
                {
                    Description = Enum.GetName(typeof(SequenceNumberTypes), type),
                    UsePrefix = false,
                    SequenceNumberType = type,
                    NextNumber = nextNumber
                });
            }
            else if (nextNumber > sequence.NextNumber)
            {
                sequence.NextNumber = nextNumber;
                _sequenceNumberRepo.Update(sequence);
            }
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
