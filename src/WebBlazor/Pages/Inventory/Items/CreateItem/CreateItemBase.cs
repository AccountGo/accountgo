using Dto.Financial;
using Dto.Inventory.Request;
using Dto.Inventory.Response;
using Dto.Purchasing.Response;
using Dto.TaxSystem;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using Radzen.Blazor;
using WebBlazor.Services.Contracts;

namespace WebBlazor.Pages.Inventory.Items.CreateItem
{
    public class CreateItemBase : InventoryComponentBase
    {
        protected Dto.Inventory.Request.CreateItem formModel { get; set; } = new();


        protected bool SubmitBtnDisabled = false;

        protected override async Task OnInitializedAsync()
        {
            SetDefaultAccounts();
        }

        protected async Task Submit()
        {
            DisableSubmitBtn(true);
            DispatchLoadingEvent(true);
            try
            {
                formModel.FillOptionalFields();
                var apiResponse = await inventoryService.CreateItem(formModel);

                if (apiResponse.Flag)
                {
                    formModel = new();
                    toastService.ShowSuccess("Item Created");
                }
                else
                {
                    toastService.ShowError(apiResponse.Message);
                }
            }
            catch(Exception ex)
            {
                toastService.ShowError(ex.Message);
                Console.WriteLine($"Error:{ex}");
            }
            finally
            {
                DispatchLoadingEvent(false);
                DisableSubmitBtn(false);
            }

        }

       
        private void DisableSubmitBtn(bool disable)
        {
            SubmitBtnDisabled = disable;
        }

        protected void SetDefaultAccounts()
        {
            var accountsLookup = lookupRx.ChartOfAcctsLookup.Value;
            var salesAccount = accountsLookup.FirstOrDefault(x => x.AccountCode == "40100");
            var expenseAccount = accountsLookup.FirstOrDefault(x => x.AccountCode == "50300");
            var inventoryAccount = accountsLookup.FirstOrDefault(x => x.AccountCode == "10800");
            
            formModel.SalesAccountId = salesAccount?.Id;
            formModel.CostOfGoodsSoldAccountId = expenseAccount?.Id;
            formModel.InventoryAccountId = inventoryAccount?.Id;
        }
    }
}
