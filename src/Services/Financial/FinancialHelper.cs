//-----------------------------------------------------------------------
// <copyright file="FinancialHelper.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using System;
using System.Diagnostics;
using System.Linq;

namespace Services.Financial
{
    public static class FinancialHelper
    {
        public static bool DrCrEqualityValidated(GeneralLedgerHeader glHeader)
        {
            decimal totalDebit = glHeader.GeneralLedgerLines.Where(d => d.DrCr == Core.Domain.DrOrCrSide.Dr).Sum(d => d.Amount);
            decimal totalCredit = glHeader.GeneralLedgerLines.Where(d => d.DrCr == Core.Domain.DrOrCrSide.Cr).Sum(d => d.Amount);
            return (totalDebit - totalCredit) == 0;
        }

        public static bool NoLineAmountIsEqualToZero(GeneralLedgerHeader glHeader)
        {
            decimal totalDebit = glHeader.GeneralLedgerLines.Where(d => d.DrCr == Core.Domain.DrOrCrSide.Dr).Sum(d => d.Amount);
            decimal totalCredit = glHeader.GeneralLedgerLines.Where(d => d.DrCr == Core.Domain.DrOrCrSide.Cr).Sum(d => d.Amount);
            if (totalDebit == 0)
                return false;
            if (totalCredit == 0)
                return false;
            return true;
        }

        public static bool NoDuplicateAccountInLines(GeneralLedgerHeader glHeader)
        {
            //TODO: implement logic. Do not allow duplicate account in gl lines
            return true;
        }

        public static bool InRange(DateTime date, DateTime start, DateTime end)
        {
            Debug.Assert(DateTime.Compare(start, end) < 1);

            if (CompareDays(date, start) > -1 && CompareDays(date, end) < 1)
            {
                return true;
            }

            return false;
        }

        public static int CompareDays(DateTime dt1, DateTime dt2)
        {
            return DateTime.Compare(DiscardTime(dt1).Value, DiscardTime(dt2).Value);
        }

        public static DateTime? DiscardTime(DateTime? d)
        {
            if (d == null)
            {
                return null;
            }

            return d.Value.Date;
        }
    }
}
