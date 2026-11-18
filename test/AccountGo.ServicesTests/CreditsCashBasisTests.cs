// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using Core.Domain;
using Services.Financial;
using Xunit;

namespace AccountGo.ServicesTests
{
    public sealed class CreditsCashBasisTests
    {
        private static readonly IReadOnlyDictionary<int, CashAccountInfo> Accounts = new Dictionary<int, CashAccountInfo>
        {
            [1] = new(1, (int)AccountClasses.Assets, "10100", "Cash"),
            [2] = new(2, (int)AccountClasses.Assets, "10120", "Accounts Receivable"),
            [3] = new(3, (int)AccountClasses.Revenue, "40100", "Sales"),
            [4] = new(4, (int)AccountClasses.Revenue, "40200", "Sales Returns"),
            [5] = new(5, (int)AccountClasses.Liabilities, "20310", "Sales Tax Payable"),
            [6] = new(6, (int)AccountClasses.Liabilities, "20410", "Customer Credits Liability"),
            [7] = new(7, (int)AccountClasses.Liabilities, "20110", "Accounts Payable"),
            [8] = new(8, (int)AccountClasses.Expense, "50200", "Operating Expense"),
            [9] = new(9, (int)AccountClasses.Assets, "10320", "Supplier Advances"),
            [10] = new(10, (int)AccountClasses.Assets, "10125", "Allowance"),
            [11] = new(11, (int)AccountClasses.Revenue, "40410", "Bad Debt Recovery"),
        };

        [Fact]
        public void X09_NonCashCreditsApplicationsAndWriteOffs_ProduceNoCashBasisRecognition()
        {
            var glLines = new List<CashGlLineInfo>
            {
                // Customer credit note: Dr Returns 100, Dr Tax 10, Cr CustomerCreditsLiability 110
                new(1, 10, new DateTime(2026, 3, 1), DocumentTypes.CustomerCreditNote, 4, DrOrCrSide.Dr, 100m),
                new(2, 10, new DateTime(2026, 3, 1), DocumentTypes.CustomerCreditNote, 5, DrOrCrSide.Dr, 10m),
                new(3, 10, new DateTime(2026, 3, 1), DocumentTypes.CustomerCreditNote, 6, DrOrCrSide.Cr, 110m),
                // SettlementApplication: Dr CustomerCreditsLiability 110, Cr AR 110
                new(4, 11, new DateTime(2026, 3, 2), DocumentTypes.SettlementApplication, 6, DrOrCrSide.Dr, 110m),
                new(5, 11, new DateTime(2026, 3, 2), DocumentTypes.SettlementApplication, 2, DrOrCrSide.Cr, 110m),
                // ReceivableWriteOff: Dr Allowance 200, Cr AR 200
                new(6, 12, new DateTime(2026, 3, 3), DocumentTypes.ReceivableWriteOff, 10, DrOrCrSide.Dr, 200m),
                new(7, 12, new DateTime(2026, 3, 3), DocumentTypes.ReceivableWriteOff, 2, DrOrCrSide.Cr, 200m),
                // SupplierCreditNote: Dr SupplierCreditAsset, Cr Expense
                new(8, 13, new DateTime(2026, 3, 4), DocumentTypes.SupplierCreditNote, 9, DrOrCrSide.Dr, 50m),
                new(9, 13, new DateTime(2026, 3, 4), DocumentTypes.SupplierCreditNote, 8, DrOrCrSide.Cr, 50m),
            };

            var input = new CashBasisRecognitionInput(
                Accounts, glLines, new HashSet<int>(),
                false, new Dictionary<int, int?>(), new Dictionary<int, int>(), Array.Empty<CashAllocationInfo>(), Array.Empty<CashInvoiceInfo>(),
                false, new Dictionary<int, int?>(), Array.Empty<CashVendorPaymentInfo>(), Array.Empty<CashBillInfo>());

            var postings = CashBasisRecognition.Recognize(input);
            Assert.Empty(postings);
        }

        [Fact]
        public void X09_CustomerRefund_ReducesRevenueAndTaxOnceOnRefundDate_AndAdvanceRecognizesBillOnce()
        {
            var glLines = new List<CashGlLineInfo>
            {
                // Customer credit note 100 + 10 tax = 110
                new(1, 10, new DateTime(2026, 3, 1), DocumentTypes.CustomerCreditNote, 4, DrOrCrSide.Dr, 100m),
                new(2, 10, new DateTime(2026, 3, 1), DocumentTypes.CustomerCreditNote, 5, DrOrCrSide.Dr, 10m),
                new(3, 10, new DateTime(2026, 3, 1), DocumentTypes.CustomerCreditNote, 6, DrOrCrSide.Cr, 110m),
                // PartyRefund: Dr CustomerCreditsLiability 110, Cr Cash 110
                new(4, 11, new DateTime(2026, 3, 10), DocumentTypes.PartyRefund, 6, DrOrCrSide.Dr, 110m),
                new(5, 11, new DateTime(2026, 3, 10), DocumentTypes.PartyRefund, 1, DrOrCrSide.Cr, 110m),
                // Bill 200 expense + 200 AP
                new(6, 12, new DateTime(2026, 3, 5), DocumentTypes.PurchaseInvoice, 8, DrOrCrSide.Dr, 200m),
                new(7, 12, new DateTime(2026, 3, 5), DocumentTypes.PurchaseInvoice, 7, DrOrCrSide.Cr, 200m),
                // Advance application: Dr AP 200, Cr SupplierAdvance 200
                new(8, 13, new DateTime(2026, 3, 12), DocumentTypes.SettlementApplication, 7, DrOrCrSide.Dr, 200m),
                new(9, 13, new DateTime(2026, 3, 12), DocumentTypes.SettlementApplication, 9, DrOrCrSide.Cr, 200m),
            };

            var input = new CashBasisRecognitionInput(
                Accounts, glLines, new HashSet<int>(),
                false, new Dictionary<int, int?>(), new Dictionary<int, int>(), Array.Empty<CashAllocationInfo>(), Array.Empty<CashInvoiceInfo>(),
                true, new Dictionary<int, int?> { [1] = 7 }, Array.Empty<CashVendorPaymentInfo>(),
                new[] { new CashBillInfo(1, 1, PurchaseInvoiceStatus.Open, 12, null) },
                CustomerCredits: new[] { new CashCustomerCreditInfo(1, 10, 110m, 6, 2) },
                PartyRefunds: new[]
                {
                    new CashPartyRefundInfo(1, PartyRefundDirection.CustomerPayout, new DateTime(2026, 3, 10), 11, null,
                        new[] { new CashPartyRefundAllocationInfo(1, null, null, 110m) })
                },
                AdvanceApplications: new[] { new CashAdvanceApplicationInfo(1, 1, new DateTime(2026, 3, 12), 200m) });

            var postings = CashBasisRecognition.Recognize(input);

            Assert.Contains(postings, p => p.Source == CashRecognitionSource.CustomerRefund && p.AccountId == 4 && p.DrCr == DrOrCrSide.Dr && p.Amount == 100m && p.Date == new DateTime(2026, 3, 10));
            Assert.Contains(postings, p => p.Source == CashRecognitionSource.CustomerRefund && p.AccountId == 5 && p.DrCr == DrOrCrSide.Dr && p.Amount == 10m && p.Date == new DateTime(2026, 3, 10));
            Assert.Contains(postings, p => p.Source == CashRecognitionSource.BillSettlement && p.AccountId == 8 && p.DrCr == DrOrCrSide.Dr && p.Amount == 200m && p.Date == new DateTime(2026, 3, 12));
            // Advance application itself (GL 13) did not double-count
            Assert.DoesNotContain(postings, p => p.GlHeaderId == 13);
        }
    }
}
