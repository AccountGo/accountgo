//-----------------------------------------------------------------------
// <copyright file="FinancialReports.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Core.Domain;

namespace Services.Financial
{
    public class TrialBalance
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class BalanceSheet
    {
        public int AccountId { get; set; }
        public int AccountClassId { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public decimal Amount { get; set; }
    }

    public class IncomeStatement
    {
        public int AccountId { get; set; }
        public bool IsExpense { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// Indirect Statement of Cash Flows. Accrual is primary; Cash basis returns the cash bridge plus a notice.
    /// Known gap before standards-complete marketing: dividends/distributions posted against retained
    /// earnings still flow through Operating (see ClassifyCashFlowAccount follow-up in FinancialService).
    /// </summary>
    public class CashFlowStatement
    {
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public AccountingBasis AccountingBasis { get; set; }
        public decimal NetIncome { get; set; }
        public decimal OperatingCashFlow { get; set; }
        public decimal InvestingCashFlow { get; set; }
        public decimal FinancingCashFlow { get; set; }
        public decimal NetChangeInCash { get; set; }
        public decimal StartingCash { get; set; }
        public decimal EndingCash { get; set; }
        public ICollection<CashFlowLine> OperatingActivities { get; set; } = new List<CashFlowLine>();
        public ICollection<CashFlowLine> InvestingActivities { get; set; } = new List<CashFlowLine>();
        public ICollection<CashFlowLine> FinancingActivities { get; set; } = new List<CashFlowLine>();
        public string Message { get; set; }
        public bool IsBalanced => AccountingBasis == AccountingBasis.Cash
            ? StartingCash + NetChangeInCash == EndingCash
            : OperatingCashFlow + InvestingCashFlow + FinancingCashFlow == NetChangeInCash;
    }

    public static class CashFlowLineTypes
    {
        public const string NonCashAdjustment = "NonCashAdjustment";
        public const string WorkingCapital = "WorkingCapital";
        public const string Operating = "Operating";
        public const string Investing = "Investing";
        public const string Financing = "Financing";
    }

    public class CashFlowLine
    {
        public int AccountId { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public string LineType { get; set; }
        public decimal Amount { get; set; }
    }

    public partial class MasterGeneralLedger
    {
        public int Id { get; set; }
        public int AccountId { get; set; }
        public int CurrencyId { get; set; }
        public string DocumentType { get; set; }
        public int TransactionNo { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public DateTime Date { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }
}
