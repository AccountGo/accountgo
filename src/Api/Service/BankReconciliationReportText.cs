// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Dto.Banking;

namespace Api.Service
{
    /// <summary>
    /// Plain-text layout of a bank reconciliation report (golden-file tests + PDF labels stay in sync).
    /// </summary>
    public static class BankReconciliationReportText
    {
        public static string Render(BankReconciliationReportDto report)
        {
            var culture = CultureInfo.InvariantCulture;
            string Money(decimal value) => value.ToString("0.00", culture);

            var sb = new StringBuilder();
            sb.AppendLine(report.CompanyName);
            sb.AppendLine($"Bank reconciliation — {report.BankName}");
            sb.AppendLine($"Statement end date: {report.StatementEndDate:yyyy-MM-dd}");
            sb.AppendLine($"Currency: {report.CurrencyCode}");
            sb.AppendLine();

            sb.AppendLine("Cleared:");
            sb.AppendLine($"  Beginning balance: {Money(report.BeginningBalance)}");
            if (report.OpeningBalanceLine != null)
            {
                var ob = report.OpeningBalanceLine;
                var obAmount = ob.In != 0m ? Money(ob.In) : Money(ob.Out);
                sb.AppendLine($"  {ob.Type}: {obAmount}");
            }
            sb.AppendLine($"  Cleared deposits: {Money(report.ClearedDeposits)}");
            sb.AppendLine($"  Cleared payments: {Money(report.ClearedPayments)}");
            sb.AppendLine($"  Cleared balance: {Money(report.ClearedBalance)}");
            sb.AppendLine($"  Statement ending balance: {Money(report.StatementEndingBalance)}");
            sb.AppendLine();

            sb.AppendLine("Balance per bank statement:");
            sb.AppendLine($"  {Money(report.StatementEndingBalance)}");
            sb.AppendLine("Add: Deposits in transit:");
            sb.AppendLine($"  {Money(report.DepositsInTransit)}");
            sb.AppendLine("Less: Outstanding payments:");
            sb.AppendLine($"  {Money(report.OutstandingPayments)}");
            sb.AppendLine("Adjusted bank balance:");
            sb.AppendLine($"  {Money(report.AdjustedBankBalance)}");
            sb.AppendLine();
            sb.AppendLine("Balance per books:");
            sb.AppendLine($"  {Money(report.BooksBalance)}");
            sb.AppendLine("Difference:");
            sb.AppendLine($"  {Money(report.Difference)}");
            sb.AppendLine();
            sb.AppendLine("Outstanding items:");
            AppendLines(sb, report.OutstandingItems, Money);
            sb.AppendLine();
            sb.AppendLine("Posted after this reconciliation was finished:");
            AppendLines(sb, report.PostedAfterFinishItems, Money);

            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(report.PreparedBy) || report.PreparedAt.HasValue)
            {
                var when = report.PreparedAt.HasValue
                    ? report.PreparedAt.Value.ToString("yyyy-MM-dd HH:mm'Z'", culture)
                    : "—";
                sb.AppendLine($"Prepared by: {report.PreparedBy ?? "—"} at {when}");
            }

            var reviewed = string.IsNullOrWhiteSpace(report.ReviewedBy) ? string.Empty : report.ReviewedBy.Trim();
            if (!string.IsNullOrEmpty(reviewed) || report.ReviewedAt.HasValue)
            {
                var when = report.ReviewedAt.HasValue
                    ? report.ReviewedAt.Value.ToString("yyyy-MM-dd HH:mm'Z'", culture)
                    : "—";
                sb.AppendLine($"Reviewed by: {reviewed} at {when}");
            }
            else
            {
                sb.AppendLine("Reviewed by:");
            }

            return sb.ToString();
        }

        private static void AppendLines(
            StringBuilder sb,
            IReadOnlyList<BankReconciliationReportLineDto> items,
            Func<decimal, string> money)
        {
            if (items.Count == 0)
            {
                sb.AppendLine("  (none)");
                return;
            }

            foreach (var item in items)
            {
                var amount = item.In != 0m ? $"+{money(item.In)}" : $"-{money(item.Out)}";
                sb.AppendLine(
                    $"  {item.Date:yyyy-MM-dd}  {item.Type}  {item.Reference ?? "—"}  {amount}  age {item.AgeDays}d");
            }
        }
    }
}
