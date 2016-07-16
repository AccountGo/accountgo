using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Financial;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class FinancialsController : Controller
    {
        private readonly IAdministrationService _adminService;
        private readonly IFinancialService _financialService;

        public FinancialsController(IAdministrationService adminService,
            IFinancialService financialService)
        {
            _adminService = adminService;
            _financialService = financialService;
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Accounts()
        {
            var accounts = _financialService.GetAccounts();
            var accountsDto = new System.Collections.Generic.List<Model.Financial.Account>();

            foreach (var account in accounts)
            {
                var accountDto = new Model.Financial.Account()
                {
                    Id = account.Id,
                    ParentAccountId = account.ParentAccountId,
                    CompanyId = account.CompanyId,
                    AccountCode = account.AccountCode,
                    AccountName = account.AccountName,
                    Description = account.Description,
                    IsCash = account.IsCash,
                    IsContraAccount = account.IsContraAccount,
                    Balance = account.Balance,
                    DebitBalance = account.DebitBalance,
                    CreditBalance = account.CreditBalance         
                };
                
                accountsDto.Add(accountDto);
            }

            return new ObjectResult(accountsDto);
        }
    }
}
