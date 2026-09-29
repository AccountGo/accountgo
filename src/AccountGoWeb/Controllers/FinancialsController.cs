using Microsoft.AspNetCore.Mvc;

namespace AccountGoWeb.Controllers
{
  //[Microsoft.AspNetCore.Authorization.Authorize]
  public class FinancialsController : BaseController
  {
    private readonly ILogger<FinancialsController> _logger;

    public FinancialsController(IConfiguration config, ILogger<FinancialsController> logger)
    {
      _baseConfig = config;
      _logger = logger;
    }

    public IActionResult AddJournalEntry()
    {
      ViewBag.PageContentHeader = "Add Journal Entry";
      return View();
    }

    public IActionResult JournalEntry(int id)
    {
      ViewBag.PageContentHeader = "Journal Entry";
      return View(model: id);
    }

    public async Task<IActionResult> Accounts()
    {
      ViewBag.PageContentHeader = "Chart of Accounts";

      using (var client = new System.Net.Http.HttpClient())
      {
        var baseUri = _baseConfig!["ApiUrl"];
        _logger.LogInformation($"+++++++++++++++ baseUri={baseUri} +++++++++++++++");
        client.BaseAddress = new System.Uri(baseUri!);
        client.DefaultRequestHeaders.Accept.Clear();
        var response = await client.GetAsync(baseUri + "financials/accounts");
        if (response.IsSuccessStatusCode)
        {
          var responseJson = await response.Content.ReadAsStringAsync();
          var accountModels = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<Models.Financial.AccountViewModel>>(responseJson);
          return View(accountModels);
        }
      }

      return View();
    }

    public async Task<IActionResult> Account(int? id = null)
    {
      Dto.Financial.Account? accountModel = null;
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
        var baseUri = _baseConfig!["ApiUrl"];
        client.BaseAddress = new System.Uri(baseUri!);
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
        var baseUri = _baseConfig!["ApiUrl"];
        client.BaseAddress = new System.Uri(baseUri!);
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
        var baseUri = _baseConfig!["ApiUrl"];
        client.BaseAddress = new System.Uri(baseUri!);
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
        var baseUri = _baseConfig!["ApiUrl"];
        client.BaseAddress = new System.Uri(baseUri!);
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
      // return View(new List<Models.BalanceSheet>()); // Use this statement to test the view with an empty balance sheet

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

    public async Task<IActionResult> IncomeStatement()
    {
      ViewBag.PageContentHeader = "Income Statement";

      using (var client = new System.Net.Http.HttpClient())
      {
        var baseUri = _baseConfig!["ApiUrl"];
        client.BaseAddress = new System.Uri(baseUri!);
        client.DefaultRequestHeaders.Accept.Clear();

        try
        {
          var response = await client.GetAsync(baseUri + "financials/incomestatement");
          if (response.IsSuccessStatusCode)
          {
            var responseJson = await response.Content.ReadAsStringAsync();
            var incomeStatementModel = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<Models.IncomeStatement>>(responseJson);
            return View(incomeStatementModel);
          }
          else
          {
            ViewBag.Error = "Failed to fetch income statement data.";
          }
        }
        catch (Exception ex)
        {
          ViewBag.Error = $"Error: {ex.Message}";
        }
      }

      return View(new List<Models.IncomeStatement>());
    }


    public IActionResult Banks()
    {
      ViewBag.PageContentHeader = "Cash/Banks";

      var banks = GetAsync<IEnumerable<Dto.Financial.Bank>>("financials/cashbanks").Result;

      return View(banks);
    }

    public async Task<IActionResult> AddBank()
    {
      ViewBag.PageContentHeader = "Add Bank";
      await PopulateBankAccounts(null);
      return View("BankForm", new Dto.Financial.Bank());
    }

    public async Task<IActionResult> EditBank(int id)
    {
      ViewBag.PageContentHeader = "Edit Bank";
      var bank = await GetAsync<Dto.Financial.Bank>($"financials/bank/{id}");
      await PopulateBankAccounts(bank?.AccountId);
      return View("BankForm", bank);
    }

    [HttpPost]
    public async Task<IActionResult> SaveBank(Dto.Financial.Bank bank)
    {
      if (!ModelState.IsValid)
      {
        ViewBag.PageContentHeader = bank.Id == 0 ? "Add Bank" : "Edit Bank";
        await PopulateBankAccounts(bank.AccountId);
        return View("BankForm", bank);
      }

      try
      {
        using (var client = new System.Net.Http.HttpClient())
        {
          var baseUri = _baseConfig!["ApiUrl"];
          client.BaseAddress = new System.Uri(baseUri!);
          client.DefaultRequestHeaders.Accept.Clear();
          client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

          var content = new System.Net.Http.StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(bank),
            System.Text.Encoding.UTF8,
            "application/json");

          System.Net.Http.HttpResponseMessage response;
          if (bank.Id == 0)
          {
            response = await client.PostAsync(baseUri + "financials/bank", content);
          }
          else
          {
            response = await client.PutAsync(baseUri + $"financials/bank/{bank.Id}", content);
          }

          if (response.IsSuccessStatusCode)
          {
            return RedirectToAction("Banks");
          }
          else
          {
            ViewBag.Error = "Failed to save bank.";
            ViewBag.PageContentHeader = bank.Id == 0 ? "Add Bank" : "Edit Bank";
            await PopulateBankAccounts(bank.AccountId);
            return View("BankForm", bank);
          }
        }
      }
      catch (Exception ex)
      {
        ViewBag.Error = $"Error: {ex.Message}";
        ViewBag.PageContentHeader = bank.Id == 0 ? "Add Bank" : "Edit Bank";
        await PopulateBankAccounts(bank.AccountId);
        return View("BankForm", bank);
      }
    }

    private async Task PopulateBankAccounts(int? selectedAccountId)
    {
      var accounts = await GetAsync<IEnumerable<Dto.Financial.Account>>("financials/accounts");
      var options = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>();
      AddBankAccountOptions(accounts, options, selectedAccountId);
      ViewBag.BankAccounts = options;
    }

    private static void AddBankAccountOptions(
      IEnumerable<Dto.Financial.Account>? accounts,
      ICollection<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> options,
      int? selectedAccountId)
    {
      if (accounts == null)
        return;

      foreach (var account in accounts)
      {
        int accountCode;
        if (int.TryParse(account.AccountCode, out accountCode) && accountCode >= 10100 && accountCode <= 10999)
        {
          options.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
          {
            Value = account.Id.ToString(),
            Text = $"{account.AccountCode} - {account.AccountName}",
            Selected = selectedAccountId == account.Id
          });
        }

        AddBankAccountOptions(account.ChildAccounts, options, selectedAccountId);
      }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteBank(int id)
    {
      try
      {
        using (var client = new System.Net.Http.HttpClient())
        {
          var baseUri = _baseConfig!["ApiUrl"];
          client.BaseAddress = new System.Uri(baseUri!);
          client.DefaultRequestHeaders.Accept.Clear();

          var response = await client.DeleteAsync(baseUri + $"financials/bank/{id}");

          if (response.IsSuccessStatusCode)
          {
            return RedirectToAction("Banks");
          }
          else
          {
            ViewBag.Error = "Failed to delete bank.";
            return RedirectToAction("Banks");
          }
        }
      }
      catch (Exception ex)
      {
        ViewBag.Error = $"Error: {ex.Message}";
        return RedirectToAction("Banks");
      }
    }



  }
}
