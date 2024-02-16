using Dto.Financial;
using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;
using WebBlazor.Services.Contracts;

namespace WebBlazor.Pages.Financials.Accounts
{
    public class ChartOfAccountsBase : FinancialsComponentBase
    {
        [Inject]
        ILookupService lookupService { get; set; } = default!;

        protected RadzenDataGrid<BaseAccount> grid;
        protected List<BaseAccount> accounts = new();
        protected bool isLoading = false;

        protected override async Task OnInitializedAsync()
        {
            isLoading = true;
            var response = await lookupService.GetAccounts();
            accounts = response.ToList();
            isLoading = false;
        }
    }
}
