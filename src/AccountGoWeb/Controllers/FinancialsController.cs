using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace AccountGoWeb.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class FinancialsController : BaseController
    {
        public FinancialsController(Microsoft.Extensions.Configuration.IConfiguration config)
        {
            _baseConfig = config;
        }

        public IActionResult AddJournalEntry()
        {
            ViewBag.PageContentHeader = "Add Journal Entry";
            return View();
        }

        public IActionResult JournalEntry(int id)
        {
            ViewBag.PageContentHeader = "Journal Entry";
            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> Accounts()
        {
            ViewBag.PageContentHeader = "Accounts";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "financials/accounts");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }

            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> Account(int? id = null)
        {
            Dto.Financial.Account accountModel = null;
            if (id == null)
            {
                accountModel = new Dto.Financial.Account();
            }
            else
            {
                accountModel = await GetAsync<Dto.Financial.Account>("financials/account?id=" + id);
            }

            ViewBag.PageContentHeader = "Account";
            return View(accountModel);
        }

        public async System.Threading.Tasks.Task<IActionResult> JournalEntries()
        {
            ViewBag.PageContentHeader = "Journal Entries";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "financials/journalentries");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }

            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> GeneralLedger()
        {
            ViewBag.PageContentHeader = "General Ledger";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "financials/generalledger");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return View(model: responseJson);
                }
            }

            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> TrialBalance()
        {
            ViewBag.PageContentHeader = "Trial Balance";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "financials/trialbalance");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var trialBalanceModel = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<Models.TrialBalance>>(responseJson);
                    return View(trialBalanceModel);
                }
            }

            return View();
        }

        public async System.Threading.Tasks.Task<IActionResult> BalanceSheet()
        {
            ViewBag.PageContentHeader = "Balance Sheet";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "financials/balancesheet");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var balanceSheetModel = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<Models.BalanceSheet>>(responseJson);
                    return View(balanceSheetModel);
                }
            }

            return View();
            //var Dto = _financialService.BalanceSheet().ToList();
            //var dt = Helpers.CollectionHelper.ConvertTo<BalanceSheet>(Dto);
            //var incomestatement = _financialService.IncomeStatement();
            //var netincome = incomestatement.Where(a => a.IsExpense == false).Sum(a => a.Amount) - incomestatement.Where(a => a.IsExpense == true).Sum(a => a.Amount);

            // TODO: Add logic to get the correct account for accumulated profit/loss. Currently, the account code is hard-coded here.
            // Solution 1: Add two columns in general ledger setting for the account id of accumulated profit and loss.
            // Solution 2: Add column to Account table to flag if account is net income (profit and loss)
            //if (netincome < 0)
            //{
            //    var loss = Dto.Where(a => a.AccountCode == "30500").FirstOrDefault();
            //    loss.Amount = netincome;
            //}
            //else
            //{
            //    var profit = Dto.Where(a => a.AccountCode == "30400").FirstOrDefault();
            //    profit.Amount = netincome;
            //}

            //return View(Dto);
        }

        public async System.Threading.Tasks.Task<IActionResult> IncomeStatement()
        {
            ViewBag.PageContentHeader = "Income Statement";

            using (var client = new System.Net.Http.HttpClient())
            {
                var baseUri = _baseConfig["ApiUrl"];
                client.BaseAddress = new System.Uri(baseUri);
                client.DefaultRequestHeaders.Accept.Clear();
                var response = await client.GetAsync(baseUri + "financials/incomestatement");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var incomeStatementModel = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<Models.IncomeStatement>>(responseJson);
                    return View(incomeStatementModel);
                }
            }

            return View();
            //var Dto = _financialService.IncomeStatement();
            //return View(Dto);
        }

        public IActionResult Banks()
        {
            ViewBag.PageContentHeader = "Cash/Banks";

            var banks = GetAsync<IEnumerable<Dto.Financial.Bank>>("financials/cashbanks").Result;

            return View(banks);
        }
    }
}
