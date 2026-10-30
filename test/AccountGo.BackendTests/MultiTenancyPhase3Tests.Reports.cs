using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Core.Domain;
using Core.Domain.Financials;
using Xunit;

namespace AccountGo.BackendTests
{
    public partial class MultiTenancyPhase3Tests
    {
        [Fact]
        public async Task Reports_FilterLedgerDates_KeepOpeningBalances_AndBalanceWithContraAccounts()
        {
            var start = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1).AddTicks(-1);
            WithDb(db =>
            {
                db.Companies.Single(c => c.Id == _companyId).ActiveModulesJson = "[\"reports\"]";
                var asset = db.Accounts.OrderBy(a => a.Id).First(a => a.AccountClassId == 1 && !a.ChildAccounts.Any());
                var equity = db.Accounts.OrderBy(a => a.Id).First(a => a.AccountClassId == 3 && !a.ChildAccounts.Any());
                var revenue = db.Accounts.OrderBy(a => a.Id).First(a => a.AccountClassId == 4 && !a.ChildAccounts.Any());
                var expense = db.Accounts.OrderBy(a => a.Id).First(a => a.AccountClassId == 5 && !a.ChildAccounts.Any());
                var contra = new Account
                {
                    CompanyId = _companyId, AccountCode = "TEST-CONTRA", AccountName = "Accumulated depreciation",
                    AccountClassId = 1, IsContraAccount = true
                };
                db.Accounts.Add(contra);
                db.GeneralLedgerHeaders.AddRange(
                    ReportEntry(start.AddTicks(-1), asset, equity, 100m),
                    ReportEntry(start, asset, revenue, 200m),
                    ReportEntry(end, expense, contra, 30m),
                    ReportEntry(end.AddTicks(1), asset, revenue, 900m));
                db.SaveChanges();
            });
            var query = "?from=" + start.ToString("O", CultureInfo.InvariantCulture) +
                "&to=" + end.ToString("O", CultureInfo.InvariantCulture);

            var trialResponse = await Send(HttpMethod.Get, "/api/Financials/trialbalance" + query);
            Assert.Equal(HttpStatusCode.OK, trialResponse.StatusCode);
            var trial = (await Json(trialResponse)).EnumerateArray().ToList();
            Assert.Equal(230m, trial.Sum(a => a.GetProperty("debit").GetDecimal()));
            Assert.Equal(230m, trial.Sum(a => a.GetProperty("credit").GetDecimal()));
            Assert.Equal(trial.Select(a => a.GetProperty("accountCode").GetString()).OrderBy(a => a),
                trial.Select(a => a.GetProperty("accountCode").GetString()));

            var incomeResponse = await Send(HttpMethod.Get, "/api/Financials/IncomeStatement" + query);
            Assert.Equal(HttpStatusCode.OK, incomeResponse.StatusCode);
            var income = (await Json(incomeResponse)).EnumerateArray().ToList();
            Assert.Equal(200m, income.Where(a => !a.GetProperty("isExpense").GetBoolean()).Sum(a => a.GetProperty("amount").GetDecimal()));
            Assert.Equal(30m, income.Where(a => a.GetProperty("isExpense").GetBoolean()).Sum(a => a.GetProperty("amount").GetDecimal()));

            var balanceResponse = await Send(HttpMethod.Get, "/api/Financials/BalanceSheet" + query);
            Assert.Equal(HttpStatusCode.OK, balanceResponse.StatusCode);
            var balance = (await Json(balanceResponse)).EnumerateArray().ToList();
            var assets = balance.Where(a => a.GetProperty("accountClassId").GetInt32() == 1).Sum(a => a.GetProperty("amount").GetDecimal());
            var liabilitiesAndEquity = balance.Where(a => a.GetProperty("accountClassId").GetInt32() != 1).Sum(a => a.GetProperty("amount").GetDecimal());
            Assert.Equal(270m, assets);
            Assert.Equal(assets, liabilitiesAndEquity);
            Assert.Contains(balance, a => a.GetProperty("accountCode").GetString() == "TEST-CONTRA" && a.GetProperty("amount").GetDecimal() == -30m);
            Assert.Contains(balance, a => a.GetProperty("accountId").GetInt32() == 0 && a.GetProperty("amount").GetDecimal() == 170m);

            var cashFlowResponse = await Send(HttpMethod.Get, "/api/Financials/CashFlow" + query);
            Assert.Equal(HttpStatusCode.OK, cashFlowResponse.StatusCode);
            var cashFlow = await Json(cashFlowResponse);
            Assert.Equal(170m, cashFlow.GetProperty("netIncome").GetDecimal());
            Assert.Equal(0m, cashFlow.GetProperty("startingCash").GetDecimal());
            Assert.Equal(0m, cashFlow.GetProperty("endingCash").GetDecimal());
            Assert.Equal(0m, cashFlow.GetProperty("netChangeInCash").GetDecimal());
            Assert.Equal(0m, cashFlow.GetProperty("operatingCashFlow").GetDecimal());
            Assert.Equal(0m, cashFlow.GetProperty("investingCashFlow").GetDecimal());
            Assert.Equal(0m, cashFlow.GetProperty("financingCashFlow").GetDecimal());
            var opActivities = cashFlow.GetProperty("operatingActivities").EnumerateArray().ToList();
            Assert.Contains(opActivities, a => a.GetProperty("accountCode").GetString() == "TEST-CONTRA" && a.GetProperty("amount").GetDecimal() == 30m);
            Assert.Contains(opActivities, a => a.GetProperty("accountCode").GetString() == "15000" && a.GetProperty("amount").GetDecimal() == -200m);
            var invActivities = cashFlow.GetProperty("investingActivities").EnumerateArray().ToList();
            Assert.Empty(invActivities);

            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/Financials/Accounts")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, "/api/Administration/financialyears")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, "/api/Financials/AddAccount", new { accountCode = "forbidden" })).StatusCode);
        }

        [Fact]
        public async Task Reports_UnclosedLoss_ReducesEquity_AndClosingDoesNotDoubleCountIncome()
        {
            WithDb(db =>
            {
                db.Companies.Single(c => c.Id == _companyId).ActiveModulesJson = "[\"reports\"]";
                var asset = db.Accounts.OrderBy(a => a.Id).First(a => a.AccountClassId == 1 && !a.ChildAccounts.Any());
                var expense = db.Accounts.OrderBy(a => a.Id).First(a => a.AccountClassId == 5 && !a.ChildAccounts.Any());
                var equity = db.Accounts.OrderBy(a => a.Id).First(a => a.AccountClassId == 3 && !a.ChildAccounts.Any());
                var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                db.GeneralLedgerHeaders.AddRange(
                    ReportEntry(day, asset, equity, 100m),
                    ReportEntry(day.AddDays(1), expense, asset, 40m),
                    ReportEntry(day.AddDays(2), equity, expense, 40m));
                db.SaveChanges();
            });
            foreach (var day in new[] { "2026-01-02", "2026-01-03" })
            {
                var response = await Send(HttpMethod.Get, "/api/Financials/BalanceSheet?to=" + day + "T23:59:59");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var rows = (await Json(response)).EnumerateArray().ToList();
                Assert.Equal(60m, rows.Where(a => a.GetProperty("accountClassId").GetInt32() == 1).Sum(a => a.GetProperty("amount").GetDecimal()));
                Assert.Equal(60m, rows.Where(a => a.GetProperty("accountClassId").GetInt32() == 3).Sum(a => a.GetProperty("amount").GetDecimal()));
                if (day == "2026-01-02")
                    Assert.Contains(rows, a => a.GetProperty("accountId").GetInt32() == 0 && a.GetProperty("amount").GetDecimal() == -40m);
                else
                    Assert.DoesNotContain(rows, a => a.GetProperty("accountId").GetInt32() == 0);
            }
        }

        [Theory]
        [InlineData("trialbalance")]
        [InlineData("IncomeStatement")]
        [InlineData("BalanceSheet")]
        [InlineData("CashFlow")]
        public async Task Reports_RequireReportsView_AndRejectInvalidDates(string action)
        {
            WithDb(db =>
            {
                db.Companies.Single(c => c.Id == _companyId).ActiveModulesJson = "[\"reports\"]";
                db.SaveChanges();
            });
            var badDate = await Send(HttpMethod.Get, "/api/Financials/" + action + "?from=2026-05-01&to=2026-04-01");
            Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
            Assert.Contains("Provide a valid date range", await badDate.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Get, "/api/Financials/" + action + "?from=invalid")).StatusCode);

            var badBasisName = await Send(HttpMethod.Get, "/api/Financials/" + action + "?basis=InvalidBasis");
            Assert.Equal(HttpStatusCode.BadRequest, badBasisName.StatusCode);
            Assert.Contains("Provide a valid accounting basis: Accrual or Cash.", await badBasisName.Content.ReadAsStringAsync());

            var badBasisNumber = await Send(HttpMethod.Get, "/api/Financials/" + action + "?basis=99");
            Assert.Equal(HttpStatusCode.BadRequest, badBasisNumber.StatusCode);
            Assert.Contains("Provide a valid accounting basis: Accrual or Cash.", await badBasisNumber.Content.ReadAsStringAsync());
            await JoinOther();
            Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Patch, Current + "/members/" + MemberId(_otherId),
                new { securityRoleIds = new[] { RoleId("Sales Representative") } })).StatusCode);
            var denied = await Send(HttpMethod.Get, "/api/Financials/" + action, token: _otherToken);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Contains("reports.view", await denied.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Reports_CashBasis_RecognizesRevenueTaxAndExpensesOnCashSettlement_AndPreservesAccrualLedger()
        {
            var jan10 = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
            var feb15 = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc);
            var mar20 = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc);
            int invoiceId = 0, voidedInvoiceId = 0, billId = 0, voidedBillId = 0;

            WithDb(db =>
            {
                db.Companies.Single(c => c.Id == _companyId).ActiveModulesJson = "[\"sales\",\"purchases\",\"financials\",\"reports\"]";
                var cash = db.Accounts.Single(a => a.AccountCode == "10111");
                var ar = db.Accounts.Single(a => a.AccountCode == "10120");
                var ap = db.Accounts.Single(a => a.AccountCode == "20110");
                var salesTax = db.Accounts.Single(a => a.AccountCode == "20300");
                var inputTax = db.Accounts.Single(a => a.AccountCode == "10160");
                var sales = db.Accounts.Single(a => a.AccountCode == "40100");
                var discount = db.Accounts.Single(a => a.AccountCode == "40400");
                var expense = db.Accounts.Single(a => a.AccountCode == "50200");

                var customerParty = new Party { CompanyId = _companyId, Name = "Cash Basis Customer", PartyType = PartyTypes.Customer };
                db.Parties.Add(customerParty);
                var vendorParty = new Party { CompanyId = _companyId, Name = "Cash Basis Vendor", PartyType = PartyTypes.Vendor };
                db.Parties.Add(vendorParty);
                db.SaveChanges();

                var customer = new Core.Domain.Sales.Customer
                {
                    CompanyId = _companyId, PartyId = customerParty.Id, No = "CUST-CB-1",
                    AccountsReceivableAccountId = ar.Id, SalesAccountId = sales.Id, SalesDiscountAccountId = discount.Id
                };
                var vendor = new Core.Domain.Purchases.Vendor
                {
                    CompanyId = _companyId, PartyId = vendorParty.Id, No = "VEND-CB-1",
                    AccountsPayableAccountId = ap.Id, PurchaseAccountId = expense.Id
                };
                db.Customers.Add(customer);
                db.Vendors.Add(vendor);
                db.SaveChanges();

                // 1. Sales Invoice on Jan 10: Gross Sales 100, Discount 10, Output Tax 9 => Net A/R 99
                var invGl = new GeneralLedgerHeader
                {
                    CompanyId = _companyId, Date = jan10, DocumentType = DocumentTypes.SalesInvoice, Description = "Invoice CB-1",
                    GeneralLedgerLines =
                    {
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = ar.Id, DrCr = DrOrCrSide.Dr, Amount = 99m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = sales.Id, DrCr = DrOrCrSide.Cr, Amount = 100m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = discount.Id, DrCr = DrOrCrSide.Dr, Amount = 10m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = salesTax.Id, DrCr = DrOrCrSide.Cr, Amount = 9m }
                    }
                };
                db.GeneralLedgerHeaders.Add(invGl);
                db.SaveChanges();

                var invoice = new Core.Domain.Sales.SalesInvoiceHeader
                {
                    CompanyId = _companyId, CustomerId = customer.Id, No = "INV-CB-1", Date = jan10,
                    Status = SalesInvoiceStatus.Closed, GeneralLedgerHeaderId = invGl.Id
                };
                db.SalesInvoiceHeaders.Add(invoice);
                db.SaveChanges();
                invGl.SourceDocumentId = invoice.Id;
                invoiceId = invoice.Id;

                // Partial receipt 1 on Feb 15: 33.00 (1/3 of 99.00 => Sales 33.33, Discount -3.33, Tax 3.00)
                var rcpt1Gl = new GeneralLedgerHeader
                {
                    CompanyId = _companyId, Date = feb15, DocumentType = DocumentTypes.SalesReceipt, Description = "Receipt 1",
                    GeneralLedgerLines =
                    {
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = cash.Id, DrCr = DrOrCrSide.Dr, Amount = 33m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = ar.Id, DrCr = DrOrCrSide.Cr, Amount = 33m }
                    }
                };
                // Remaining receipt 2 on Mar 20: 66.00 (brings cumulative to 99.00 => Sales 66.67, Discount -6.67, Tax 6.00)
                var rcpt2Gl = new GeneralLedgerHeader
                {
                    CompanyId = _companyId, Date = mar20, DocumentType = DocumentTypes.SalesReceipt, Description = "Receipt 2",
                    GeneralLedgerLines =
                    {
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = cash.Id, DrCr = DrOrCrSide.Dr, Amount = 66m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = ar.Id, DrCr = DrOrCrSide.Cr, Amount = 66m }
                    }
                };
                db.GeneralLedgerHeaders.AddRange(rcpt1Gl, rcpt2Gl);
                db.SaveChanges();

                var rcpt1 = new Core.Domain.Sales.SalesReceiptHeader
                {
                    CompanyId = _companyId, CustomerId = customer.Id, No = "RCPT-CB-1", Date = feb15,
                    Amount = 33m, AccountToDebitId = cash.Id, GeneralLedgerHeaderId = rcpt1Gl.Id
                };
                var rcpt2 = new Core.Domain.Sales.SalesReceiptHeader
                {
                    CompanyId = _companyId, CustomerId = customer.Id, No = "RCPT-CB-2", Date = mar20,
                    Amount = 66m, AccountToDebitId = cash.Id, GeneralLedgerHeaderId = rcpt2Gl.Id
                };
                db.SalesReceiptHeaders.AddRange(rcpt1, rcpt2);
                db.SaveChanges();

                db.CustomerAllocations.AddRange(
                    new Core.Domain.Sales.CustomerAllocation
                    {
                        CompanyId = _companyId, CustomerId = customer.Id, SalesInvoiceHeaderId = invoice.Id,
                        SalesReceiptHeaderId = rcpt1.Id, Date = feb15, Amount = 33m
                    },
                    new Core.Domain.Sales.CustomerAllocation
                    {
                        CompanyId = _companyId, CustomerId = customer.Id, SalesInvoiceHeaderId = invoice.Id,
                        SalesReceiptHeaderId = rcpt2.Id, Date = mar20, Amount = 66m
                    });

                // 2. Voided Sales Invoice on Jan 10 (must net to 0 on both Accrual and Cash basis)
                var voidOrigGl = new GeneralLedgerHeader
                {
                    CompanyId = _companyId, Date = jan10, DocumentType = DocumentTypes.SalesInvoice, Description = "Voided Invoice",
                    GeneralLedgerLines =
                    {
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = ar.Id, DrCr = DrOrCrSide.Dr, Amount = 50m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = sales.Id, DrCr = DrOrCrSide.Cr, Amount = 50m }
                    }
                };
                db.GeneralLedgerHeaders.Add(voidOrigGl);
                db.SaveChanges();
                var voidRevGl = new GeneralLedgerHeader
                {
                    CompanyId = _companyId, Date = jan10, DocumentType = DocumentTypes.SalesInvoice, Description = "Void of invoice",
                    ReversedGeneralLedgerHeaderId = voidOrigGl.Id,
                    GeneralLedgerLines =
                    {
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = ar.Id, DrCr = DrOrCrSide.Cr, Amount = 50m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = sales.Id, DrCr = DrOrCrSide.Dr, Amount = 50m }
                    }
                };
                db.GeneralLedgerHeaders.Add(voidRevGl);
                db.SaveChanges();
                var voidedInv = new Core.Domain.Sales.SalesInvoiceHeader
                {
                    CompanyId = _companyId, CustomerId = customer.Id, No = "INV-CB-VOID", Date = jan10,
                    Status = SalesInvoiceStatus.Void, GeneralLedgerHeaderId = voidOrigGl.Id, VoidGeneralLedgerHeaderId = voidRevGl.Id
                };
                db.SalesInvoiceHeaders.Add(voidedInv);
                db.SaveChanges();
                voidOrigGl.SourceDocumentId = voidedInv.Id;
                voidRevGl.SourceDocumentId = voidedInv.Id;
                voidedInvoiceId = voidedInv.Id;

                // 3. Vendor Bill on Jan 10: Expense 40, Input Tax 4 => A/P 44; paid 22 on Feb 15 and 22 on Mar 20
                var billGl = new GeneralLedgerHeader
                {
                    CompanyId = _companyId, Date = jan10, DocumentType = DocumentTypes.PurchaseInvoice, Description = "Bill CB-1",
                    GeneralLedgerLines =
                    {
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = expense.Id, DrCr = DrOrCrSide.Dr, Amount = 40m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = inputTax.Id, DrCr = DrOrCrSide.Dr, Amount = 4m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = ap.Id, DrCr = DrOrCrSide.Cr, Amount = 44m }
                    }
                };
                var pay1Gl = new GeneralLedgerHeader
                {
                    CompanyId = _companyId, Date = feb15, DocumentType = DocumentTypes.PurchaseInvoicePayment, Description = "Bill Payment 1",
                    GeneralLedgerLines =
                    {
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = ap.Id, DrCr = DrOrCrSide.Dr, Amount = 22m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = cash.Id, DrCr = DrOrCrSide.Cr, Amount = 22m }
                    }
                };
                var pay2Gl = new GeneralLedgerHeader
                {
                    CompanyId = _companyId, Date = mar20, DocumentType = DocumentTypes.PurchaseInvoicePayment, Description = "Bill Payment 2",
                    GeneralLedgerLines =
                    {
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = ap.Id, DrCr = DrOrCrSide.Dr, Amount = 22m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = cash.Id, DrCr = DrOrCrSide.Cr, Amount = 22m }
                    }
                };
                db.GeneralLedgerHeaders.AddRange(billGl, pay1Gl, pay2Gl);
                db.SaveChanges();

                var bill = new Core.Domain.Purchases.PurchaseInvoiceHeader
                {
                    CompanyId = _companyId, VendorId = vendor.Id, No = "BILL-CB-1", VendorInvoiceNo = "V-1", Date = jan10,
                    Status = PurchaseInvoiceStatus.Paid, GeneralLedgerHeaderId = billGl.Id
                };
                db.PurchaseInvoiceHeaders.Add(bill);
                db.SaveChanges();
                billGl.SourceDocumentId = bill.Id;
                pay1Gl.SourceDocumentId = bill.Id;
                pay2Gl.SourceDocumentId = bill.Id;
                billId = bill.Id;

                db.VendorPayments.AddRange(
                    new Core.Domain.Purchases.VendorPayment
                    {
                        CompanyId = _companyId, VendorId = vendor.Id, PurchaseInvoiceHeaderId = bill.Id,
                        GeneralLedgerHeaderId = pay1Gl.Id, AccountId = cash.Id, Date = feb15, Amount = 22m, No = "VP-1"
                    },
                    new Core.Domain.Purchases.VendorPayment
                    {
                        CompanyId = _companyId, VendorId = vendor.Id, PurchaseInvoiceHeaderId = bill.Id,
                        GeneralLedgerHeaderId = pay2Gl.Id, AccountId = cash.Id, Date = mar20, Amount = 22m, No = "VP-2"
                    });

                // 4. Voided Vendor Bill on Jan 10
                var voidBillOrigGl = new GeneralLedgerHeader
                {
                    CompanyId = _companyId, Date = jan10, DocumentType = DocumentTypes.PurchaseInvoice, Description = "Voided Bill",
                    GeneralLedgerLines =
                    {
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = expense.Id, DrCr = DrOrCrSide.Dr, Amount = 25m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = ap.Id, DrCr = DrOrCrSide.Cr, Amount = 25m }
                    }
                };
                db.GeneralLedgerHeaders.Add(voidBillOrigGl);
                db.SaveChanges();
                var voidBillRevGl = new GeneralLedgerHeader
                {
                    CompanyId = _companyId, Date = jan10, DocumentType = DocumentTypes.PurchaseInvoice, Description = "Void of bill",
                    ReversedGeneralLedgerHeaderId = voidBillOrigGl.Id,
                    GeneralLedgerLines =
                    {
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = expense.Id, DrCr = DrOrCrSide.Cr, Amount = 25m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = ap.Id, DrCr = DrOrCrSide.Dr, Amount = 25m }
                    }
                };
                db.GeneralLedgerHeaders.Add(voidBillRevGl);
                db.SaveChanges();
                var voidedBill = new Core.Domain.Purchases.PurchaseInvoiceHeader
                {
                    CompanyId = _companyId, VendorId = vendor.Id, No = "BILL-CB-VOID", VendorInvoiceNo = "V-VOID", Date = jan10,
                    Status = PurchaseInvoiceStatus.Void, GeneralLedgerHeaderId = voidBillOrigGl.Id, VoidGeneralLedgerHeaderId = voidBillRevGl.Id
                };
                db.PurchaseInvoiceHeaders.Add(voidedBill);
                db.SaveChanges();
                voidBillOrigGl.SourceDocumentId = voidedBill.Id;
                voidBillRevGl.SourceDocumentId = voidedBill.Id;
                voidedBillId = voidedBill.Id;
                db.SaveChanges();
            });

            // Period 1 (Jan 2026): Invoices/bills issued, no cash collected/paid yet
            var janQuery = "?from=2026-01-01T00:00:00&to=2026-01-31T23:59:59";
            var janAccrualIncome = (await Json(await Send(HttpMethod.Get, "/api/Financials/IncomeStatement" + janQuery + "&basis=Accrual"))).EnumerateArray().ToList();
            Assert.Equal(90m, janAccrualIncome.Where(a => !a.GetProperty("isExpense").GetBoolean()).Sum(a => a.GetProperty("amount").GetDecimal()));
            Assert.Equal(40m, janAccrualIncome.Where(a => a.GetProperty("isExpense").GetBoolean()).Sum(a => a.GetProperty("amount").GetDecimal()));

            var janCashTrial = (await Json(await Send(HttpMethod.Get, "/api/Financials/trialbalance" + janQuery + "&basis=Cash"))).EnumerateArray().ToList();
            Assert.Empty(janCashTrial);

            var janCashIncome = (await Json(await Send(HttpMethod.Get, "/api/Financials/IncomeStatement" + janQuery + "&basis=Cash"))).EnumerateArray().ToList();
            Assert.Equal(0m, janCashIncome.Sum(a => a.GetProperty("amount").GetDecimal()));

            // Period 2 (Through Feb 2026): 1/3 of invoice collected (33.00) and 1/2 of bill paid (22.00)
            var febQuery = "?from=2026-01-01T00:00:00&to=2026-02-28T23:59:59";
            var febCashTrial = (await Json(await Send(HttpMethod.Get, "/api/Financials/trialbalance" + febQuery + "&basis=Cash"))).EnumerateArray().ToList();
            Assert.Equal(febCashTrial.Sum(a => a.GetProperty("debit").GetDecimal()), febCashTrial.Sum(a => a.GetProperty("credit").GetDecimal()));
            Assert.DoesNotContain(febCashTrial, a => a.GetProperty("accountCode").GetString() == "10120");
            Assert.DoesNotContain(febCashTrial, a => a.GetProperty("accountCode").GetString() == "20110");
            Assert.Single(febCashTrial, a => a.GetProperty("accountCode").GetString() == "40100" && a.GetProperty("credit").GetDecimal() == 33.33m);
            Assert.Single(febCashTrial, a => a.GetProperty("accountCode").GetString() == "40400" && a.GetProperty("debit").GetDecimal() == 3.33m);
            Assert.Single(febCashTrial, a => a.GetProperty("accountCode").GetString() == "20300" && a.GetProperty("credit").GetDecimal() == 3.00m);
            Assert.Single(febCashTrial, a => a.GetProperty("accountCode").GetString() == "50200" && a.GetProperty("debit").GetDecimal() == 20.00m);
            Assert.Single(febCashTrial, a => a.GetProperty("accountCode").GetString() == "10160" && a.GetProperty("debit").GetDecimal() == 2.00m);

            var febCashBalance = (await Json(await Send(HttpMethod.Get, "/api/Financials/BalanceSheet?to=2026-02-28T23:59:59&basis=Cash"))).EnumerateArray().ToList();
            var febAssets = febCashBalance.Where(a => a.GetProperty("accountClassId").GetInt32() == 1).Sum(a => a.GetProperty("amount").GetDecimal());
            var febLiabEquity = febCashBalance.Where(a => a.GetProperty("accountClassId").GetInt32() != 1).Sum(a => a.GetProperty("amount").GetDecimal());
            Assert.Equal(13.00m, febAssets); // Cash (33 - 22 = 11) + Input Tax Receivable (2) = 13
            Assert.Equal(febAssets, febLiabEquity);

            // Period 3 (Through Mar 2026): 100% collected and paid; Cash Basis converges with Accrual Basis
            var marQuery = "?from=2026-01-01T00:00:00&to=2026-03-31T23:59:59";
            var marCashIncome = (await Json(await Send(HttpMethod.Get, "/api/Financials/IncomeStatement" + marQuery + "&basis=Cash"))).EnumerateArray().ToList();
            var marAccrualIncome = (await Json(await Send(HttpMethod.Get, "/api/Financials/IncomeStatement" + marQuery + "&basis=Accrual"))).EnumerateArray().ToList();
            Assert.Equal(90m, marCashIncome.Where(a => !a.GetProperty("isExpense").GetBoolean()).Sum(a => a.GetProperty("amount").GetDecimal()));
            Assert.Equal(40m, marCashIncome.Where(a => a.GetProperty("isExpense").GetBoolean()).Sum(a => a.GetProperty("amount").GetDecimal()));
            Assert.Equal(
                marAccrualIncome.Sum(a => a.GetProperty("amount").GetDecimal()),
                marCashIncome.Sum(a => a.GetProperty("amount").GetDecimal()));

            var marCashBalance = (await Json(await Send(HttpMethod.Get, "/api/Financials/BalanceSheet?to=2026-03-31T23:59:59&basis=Cash"))).EnumerateArray().ToList();
            var marAssets = marCashBalance.Where(a => a.GetProperty("accountClassId").GetInt32() == 1).Sum(a => a.GetProperty("amount").GetDecimal());
            var marLiabEquity = marCashBalance.Where(a => a.GetProperty("accountClassId").GetInt32() != 1).Sum(a => a.GetProperty("amount").GetDecimal());
            Assert.Equal(59.00m, marAssets); // Cash (99 - 44 = 55) + Input Tax Receivable (4) = 59
            Assert.Equal(marAssets, marLiabEquity);

            WithDb(db =>
            {
                Assert.Contains(db.GeneralLedgerHeaders, g => g.SourceDocumentId == invoiceId && g.ReversedGeneralLedgerHeaderId == null);
                Assert.Contains(db.GeneralLedgerHeaders, g => g.SourceDocumentId == voidedInvoiceId && g.ReversedGeneralLedgerHeaderId != null);
                Assert.Contains(db.GeneralLedgerHeaders, g => g.SourceDocumentId == billId && g.ReversedGeneralLedgerHeaderId == null);
                Assert.Contains(db.GeneralLedgerHeaders, g => g.SourceDocumentId == voidedBillId && g.ReversedGeneralLedgerHeaderId != null);
            });
        }

        [Fact]
        public async Task Reports_CashBasis_CapitalizesStockBillsOnApproval_AndHandlesSaveAllocationAndReceiptCreditCap()
        {
            var jan10 = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
            var jan20 = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);
            var feb10 = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc);
            var mar10 = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

            int vendorId = 0, customerId = 0, stockItemId = 0, serviceItemId = 0, cashAccountId = 0, advancesAccountId = 0;
            int billId = 0, stockInvoiceId = 0, serviceInvoice2Id = 0, depositReceiptId = 0;

            WithDb(db =>
            {
                db.Companies.Single(c => c.Id == _companyId).ActiveModulesJson = "[\"sales\",\"purchases\",\"inventory\",\"financials\",\"reports\"]";
                var cash = db.Accounts.Single(a => a.AccountCode == "10111");
                var ar = db.Accounts.Single(a => a.AccountCode == "10120");
                var inventory = db.Accounts.Single(a => a.AccountCode == "10800");
                var ap = db.Accounts.Single(a => a.AccountCode == "20110");
                var advances = db.Accounts.Single(a => a.AccountCode == "20120");
                var sales = db.Accounts.Single(a => a.AccountCode == "40100");
                var discount = db.Accounts.Single(a => a.AccountCode == "40400");
                var cogs = db.Accounts.Single(a => a.AccountCode == "50300");
                var adjustment = db.Accounts.SingleOrDefault(a => a.AccountCode == "50500") ?? db.Accounts.Single(a => a.AccountCode == "50310");
                var measurement = db.Measurements.First();

                cashAccountId = cash.Id;
                advancesAccountId = advances.Id;

                var vendorParty = new Party { CompanyId = _companyId, Name = "Stock Supplier", PartyType = PartyTypes.Vendor };
                var customerParty = new Party { CompanyId = _companyId, Name = "Deposit Customer", PartyType = PartyTypes.Customer };
                db.Parties.AddRange(vendorParty, customerParty);
                db.SaveChanges();

                var vendor = new Core.Domain.Purchases.Vendor
                {
                    CompanyId = _companyId, PartyId = vendorParty.Id, No = "VEND-STK-1",
                    AccountsPayableAccountId = ap.Id, PurchaseAccountId = cogs.Id
                };
                var customer = new Core.Domain.Sales.Customer
                {
                    CompanyId = _companyId, PartyId = customerParty.Id, No = "CUST-DEP-1",
                    AccountsReceivableAccountId = ar.Id, CustomerAdvancesAccountId = advances.Id,
                    SalesAccountId = sales.Id, SalesDiscountAccountId = discount.Id
                };
                db.Vendors.Add(vendor);
                db.Customers.Add(customer);

                var stockCat = new Core.Domain.Items.ItemCategory
                {
                    CompanyId = _companyId, Name = "Purchased Stock", ItemType = ItemTypes.Purchased,
                    InventoryAccountId = inventory.Id, CostOfGoodsSoldAccountId = cogs.Id,
                    SalesAccountId = sales.Id, AdjustmentAccountId = adjustment.Id, MeasurementId = measurement.Id
                };
                var serviceCat = new Core.Domain.Items.ItemCategory
                {
                    CompanyId = _companyId, Name = "Service Cat", ItemType = ItemTypes.Service,
                    SalesAccountId = sales.Id, MeasurementId = measurement.Id
                };
                db.ItemCategories.AddRange(stockCat, serviceCat);
                db.SaveChanges();

                var stockItem = new Core.Domain.Items.Item
                {
                    CompanyId = _companyId, No = "ITM-STK-1", Code = "ITM-STK-1", Description = "Widget",
                    ItemCategoryId = stockCat.Id, SellMeasurementId = measurement.Id, PurchaseMeasurementId = measurement.Id, SmallestMeasurementId = measurement.Id,
                    SalesAccountId = sales.Id, InventoryAccountId = inventory.Id, CostOfGoodsSoldAccountId = cogs.Id, InventoryAdjustmentAccountId = adjustment.Id,
                    Cost = 20m, Price = 50m
                };
                var serviceItem = new Core.Domain.Items.Item
                {
                    CompanyId = _companyId, No = "ITM-SVC-1", Code = "ITM-SVC-1", Description = "Consulting",
                    ItemCategoryId = serviceCat.Id, SellMeasurementId = measurement.Id, PurchaseMeasurementId = measurement.Id, SmallestMeasurementId = measurement.Id,
                    SalesAccountId = sales.Id, InventoryAccountId = inventory.Id, CostOfGoodsSoldAccountId = cogs.Id, InventoryAdjustmentAccountId = adjustment.Id,
                    Cost = 0m, Price = 80m
                };
                db.Items.AddRange(stockItem, serviceItem);
                db.SaveChanges();

                vendorId = vendor.Id;
                customerId = customer.Id;
                stockItemId = stockItem.Id;
                serviceItemId = serviceItem.Id;
            });

            // 1. Create and approve a stock bill on Jan 10: 10 units @ $20 = $200 Inventory
            var createBillResp = await Send(HttpMethod.Post, "/api/Purchasing/SaveBill", new
            {
                vendorId,
                vendorInvoiceNo = "STK-BILL-1",
                date = jan10,
                lines = new[]
                {
                    new { itemId = stockItemId, description = "Widget", quantity = 10m, amount = 20m }
                }
            });
            Assert.Equal(HttpStatusCode.OK, createBillResp.StatusCode);
            billId = (await Json(createBillResp)).GetProperty("id").GetInt32();

            var approveResp = await Send(HttpMethod.Post, $"/api/Purchasing/ApproveBill?id={billId}");
            Assert.Equal(HttpStatusCode.OK, approveResp.StatusCode);

            // Verify on Jan 15 (before selling or paying): Inventory (10800) is +$200 and A/P (20110) is +$200 on Cash Basis
            var jan15Balance = (await Json(await Send(HttpMethod.Get, "/api/Financials/BalanceSheet?to=2026-01-15T23:59:59&basis=Cash"))).EnumerateArray().ToList();
            Assert.Single(jan15Balance, a => a.GetProperty("accountCode").GetString() == "10800" && a.GetProperty("amount").GetDecimal() == 200m);
            Assert.Single(jan15Balance, a => a.GetProperty("accountCode").GetString() == "20110" && a.GetProperty("amount").GetDecimal() == 200m);
            Assert.Equal(
                jan15Balance.Where(a => a.GetProperty("accountClassId").GetInt32() == 1).Sum(a => a.GetProperty("amount").GetDecimal()),
                jan15Balance.Where(a => a.GetProperty("accountClassId").GetInt32() != 1).Sum(a => a.GetProperty("amount").GetDecimal()));

            // 2. Sell 4 units @ $50 = $200 (cost 4 * $20 = $80) on Jan 20, before paying the supplier
            WithDb(db =>
            {
                var measurementId = db.Measurements.First().Id;
                var inv1 = new Core.Domain.Sales.SalesInvoiceHeader
                {
                    CompanyId = _companyId, CustomerId = customerId, Date = jan20, Status = SalesInvoiceStatus.Draft,
                    SalesInvoiceLines =
                    {
                        new Core.Domain.Sales.SalesInvoiceLine
                        {
                            CompanyId = _companyId, ItemId = stockItemId, MeasurementId = measurementId,
                            Quantity = 4m, Amount = 50m, Discount = 0m
                        }
                    }
                };
                var inv2 = new Core.Domain.Sales.SalesInvoiceHeader
                {
                    CompanyId = _companyId, CustomerId = customerId, Date = jan20, Status = SalesInvoiceStatus.Draft,
                    SalesInvoiceLines =
                    {
                        new Core.Domain.Sales.SalesInvoiceLine
                        {
                            CompanyId = _companyId, ItemId = serviceItemId, MeasurementId = measurementId,
                            Quantity = 1m, Amount = 80m, Discount = 0m
                        }
                    }
                };
                db.SalesInvoiceHeaders.AddRange(inv1, inv2);
                db.SaveChanges();
                stockInvoiceId = inv1.Id;
                serviceInvoice2Id = inv2.Id;
            });

            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={stockInvoiceId}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Post, $"/api/Sales/IssueSalesInvoice?id={serviceInvoice2Id}")).StatusCode);

            // Verify on Jan 31 (after sale, still unpaid by customer and supplier unpaid):
            // Inventory is $200 - $80 = +$120 (never negative, ties to subledger 6 units * $20), COGS is $80, A/P is $200, Revenue is $0
            var jan31Balance = (await Json(await Send(HttpMethod.Get, "/api/Financials/BalanceSheet?to=2026-01-31T23:59:59&basis=Cash"))).EnumerateArray().ToList();
            Assert.Single(jan31Balance, a => a.GetProperty("accountCode").GetString() == "10800" && a.GetProperty("amount").GetDecimal() == 120m);
            Assert.Single(jan31Balance, a => a.GetProperty("accountCode").GetString() == "20110" && a.GetProperty("amount").GetDecimal() == 200m);
            Assert.Equal(
                jan31Balance.Where(a => a.GetProperty("accountClassId").GetInt32() == 1).Sum(a => a.GetProperty("amount").GetDecimal()),
                jan31Balance.Where(a => a.GetProperty("accountClassId").GetInt32() != 1).Sum(a => a.GetProperty("amount").GetDecimal()));

            var jan31Income = (await Json(await Send(HttpMethod.Get, "/api/Financials/IncomeStatement?from=2026-01-01T00:00:00&to=2026-01-31T23:59:59&basis=Cash"))).EnumerateArray().ToList();
            Assert.Equal(0m, jan31Income.Where(a => !a.GetProperty("isExpense").GetBoolean()).Sum(a => a.GetProperty("amount").GetDecimal()));
            Assert.Single(jan31Income, a => a.GetProperty("accountCode").GetString() == "50300" && a.GetProperty("amount").GetDecimal() == 80m);

            // 3. Pay 50% ($100) of the stock bill on Feb 10: Inventory stays $120, A/P drops from $200 to $100
            var payBillResp = await Send(HttpMethod.Post, "/api/Purchasing/PayBill", new
            {
                billId,
                accountId = cashAccountId,
                amount = 100m,
                date = feb10,
                paymentMethod = "Bank Transfer"
            });
            Assert.Equal(HttpStatusCode.OK, payBillResp.StatusCode);

            var febBalance = (await Json(await Send(HttpMethod.Get, "/api/Financials/BalanceSheet?to=2026-02-28T23:59:59&basis=Cash"))).EnumerateArray().ToList();
            Assert.Single(febBalance, a => a.GetProperty("accountCode").GetString() == "10800" && a.GetProperty("amount").GetDecimal() == 120m);
            Assert.Single(febBalance, a => a.GetProperty("accountCode").GetString() == "20110" && a.GetProperty("amount").GetDecimal() == 100m);
            Assert.Equal(
                febBalance.Where(a => a.GetProperty("accountClassId").GetInt32() == 1).Sum(a => a.GetProperty("amount").GetDecimal()),
                febBalance.Where(a => a.GetProperty("accountClassId").GetInt32() != 1).Sum(a => a.GetProperty("amount").GetDecimal()));

            // 4. Record a customer deposit of $200 (Dr Cash, Cr Customer Advances) on Mar 10
            // and apply it to stockInvoiceId ($200) via POST /api/Sales/SaveAllocation
            WithDb(db =>
            {
                var depGl = new GeneralLedgerHeader
                {
                    CompanyId = _companyId, Date = mar10, DocumentType = DocumentTypes.SalesReceipt, Description = "Customer deposit",
                    GeneralLedgerLines =
                    {
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = cashAccountId, DrCr = DrOrCrSide.Dr, Amount = 200m },
                        new GeneralLedgerLine { CompanyId = _companyId, AccountId = advancesAccountId, DrCr = DrOrCrSide.Cr, Amount = 200m }
                    }
                };
                db.GeneralLedgerHeaders.Add(depGl);
                db.SaveChanges();

                var depReceipt = new Core.Domain.Sales.SalesReceiptHeader
                {
                    CompanyId = _companyId, CustomerId = customerId, No = "DEP-1", Date = mar10,
                    Amount = 200m, AccountToDebitId = cashAccountId, GeneralLedgerHeaderId = depGl.Id,
                    SalesReceiptLines =
                    {
                        new Core.Domain.Sales.SalesReceiptLine
                        {
                            CompanyId = _companyId, AccountToCreditId = advancesAccountId,
                            Quantity = 1m, Amount = 200m, AmountPaid = 200m
                        }
                    }
                };
                db.SalesReceiptHeaders.Add(depReceipt);
                db.SaveChanges();
                depositReceiptId = depReceipt.Id;
            });

            var saveAllocResp = await Send(HttpMethod.Post, "/api/Sales/SaveAllocation", new
            {
                CustomerId = customerId,
                ReceiptId = depositReceiptId,
                Date = mar10,
                AllocationLines = new[]
                {
                    new { InvoiceId = stockInvoiceId, AmountToAllocate = 200m }
                }
            });
            Assert.Equal(HttpStatusCode.OK, saveAllocResp.StatusCode);

            // Verify allocation linked its GL header and SourceDocumentId
            WithDb(db =>
            {
                var alloc = db.CustomerAllocations.Single(a => a.SalesInvoiceHeaderId == stockInvoiceId && a.SalesReceiptHeaderId == depositReceiptId);
                Assert.NotNull(alloc.GeneralLedgerHeaderId);
                var allocGl = db.GeneralLedgerHeaders.Single(g => g.Id == alloc.GeneralLedgerHeaderId.Value);
                Assert.Equal(DocumentTypes.CustomerAllocation, allocGl.DocumentType);
                Assert.Equal(alloc.Id, allocGl.SourceDocumentId);
            });

            // Over-allocating the already-exhausted receipt via SaveAllocation is rejected
            var overAllocResp = await Send(HttpMethod.Post, "/api/Sales/SaveAllocation", new
            {
                CustomerId = customerId,
                ReceiptId = depositReceiptId,
                Date = mar10,
                AllocationLines = new[]
                {
                    new { InvoiceId = serviceInvoice2Id, AmountToAllocate = 80m }
                }
            });
            Assert.Equal(HttpStatusCode.BadRequest, overAllocResp.StatusCode);

            // Even if a rogue over-allocation row exists in the DB against the same $200 receipt, CashBasisTrialBalance caps
            // suppression at the receipt credit ($200), does not recognize excess revenue, and stays in balance
            WithDb(db =>
            {
                db.CustomerAllocations.Add(new Core.Domain.Sales.CustomerAllocation
                {
                    CompanyId = _companyId, CustomerId = customerId, SalesInvoiceHeaderId = serviceInvoice2Id,
                    SalesReceiptHeaderId = depositReceiptId, Date = mar10, Amount = 80m
                });
                db.SaveChanges();
            });

            var marCashTrial = (await Json(await Send(HttpMethod.Get, "/api/Financials/trialbalance?from=2026-01-01T00:00:00&to=2026-03-31T23:59:59&basis=Cash"))).EnumerateArray().ToList();
            Assert.Equal(
                marCashTrial.Sum(a => a.GetProperty("debit").GetDecimal()),
                marCashTrial.Sum(a => a.GetProperty("credit").GetDecimal()));
            // Customer Advances (20120) and Accounts Receivable (10120) are both 0; Sales (40100) is capped at $200
            Assert.DoesNotContain(marCashTrial, a => a.GetProperty("accountCode").GetString() == "20120");
            Assert.DoesNotContain(marCashTrial, a => a.GetProperty("accountCode").GetString() == "10120");
            Assert.Single(marCashTrial, a => a.GetProperty("accountCode").GetString() == "40100" && a.GetProperty("credit").GetDecimal() == 200m);
        }

        private GeneralLedgerHeader ReportEntry(DateTime date, Account debit, Account credit, decimal amount)
        {
            return new GeneralLedgerHeader
            {
                CompanyId = _companyId, Date = date, Description = "Report test", DocumentType = DocumentTypes.JournalEntry,
                GeneralLedgerLines =
                {
                    new GeneralLedgerLine { CompanyId = _companyId, Account = debit, DrCr = DrOrCrSide.Dr, Amount = amount },
                    new GeneralLedgerLine { CompanyId = _companyId, Account = credit, DrCr = DrOrCrSide.Cr, Amount = amount }
                }
            };
        }
    }
}
