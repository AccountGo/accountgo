using Dto.Financial;
using Dto.Inventory;
using Dto.Inventory.Request;
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
        protected CreateItemRequest formModel { get; set; } = new();

        #region lookup data source
        protected IEnumerable<BaseAccount> accountsLookup { get; private set; } = Enumerable.Empty<BaseAccount>();
        protected IEnumerable<ItemTaxGroup> itemTaxGroupLookup { get; private set; } = Enumerable.Empty<ItemTaxGroup>();
        protected IEnumerable<Measurement> measurementsLookup { get; private set; } = Enumerable.Empty<Measurement>();
        protected IEnumerable<ItemCategory> itemCategoriesLookup { get; private set; } = Enumerable.Empty<ItemCategory>();
        protected IEnumerable<GetVendorResponse> vendorsLookup { get; private set; } = Enumerable.Empty<GetVendorResponse>();
        #endregion

        protected IEnumerable<BaseAccount> salesAccountDropdown { get; set; } = Enumerable.Empty<BaseAccount>();
        protected IEnumerable<BaseAccount> expenseAccountDropdown { get; set; } = Enumerable.Empty<BaseAccount>();
        protected IEnumerable<BaseAccount> inventoryAccountDropdown { get; set; } = Enumerable.Empty<BaseAccount>();
        protected IEnumerable<ItemTaxGroup> itemTaxGroupDropdown { get; set; } = Enumerable.Empty<ItemTaxGroup>();
        protected IEnumerable<Measurement> measurementDropdown { get; set; } = Enumerable.Empty<Measurement>();
        protected IEnumerable<ItemCategory> itemCategoriesDropdown { get; set; } = Enumerable.Empty<ItemCategory>();
        protected IEnumerable<GetVendorResponse> vendorsDropdown { get; set; } = Enumerable.Empty<GetVendorResponse>();
        protected bool SubmitBtnDisabled = false;

        protected override async Task OnInitializedAsync()
        {

            DispatchLoadingEvent(true);
            try
            {
                await InitializeLookUps();

                itemTaxGroupDropdown = itemTaxGroupLookup;
                measurementDropdown = measurementsLookup;
                salesAccountDropdown = accountsLookup;
                expenseAccountDropdown = accountsLookup;
                inventoryAccountDropdown = accountsLookup;
                itemCategoriesDropdown = itemCategoriesLookup;
                vendorsDropdown = vendorsLookup;
                SetDefaultAccounts();
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"Error:{ex}");
            }
            finally
            {
                DispatchLoadingEvent(false);
            }
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

        protected virtual async Task InitializeLookUps()
        {
            var task1 = lookupService.GetAccounts();
            var task2 = taxService.GetItemTaxGroups();
            var task3 = lookupService.GetMeasurements();
            var task4 = lookupService.GetItemCategories();
            var task5 = lookupService.GetVendors();
            try
            {
                await Task.WhenAll(task1, task2, task3, task4, task5);
                accountsLookup = task1.Result;
                itemTaxGroupLookup = task2.Result;
                measurementsLookup = task3.Result;
                itemCategoriesLookup = task4.Result;
                vendorsLookup = task5.Result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex}");
            }
        }
        private void DisableSubmitBtn(bool disable)
        {
            SubmitBtnDisabled = disable;
        }

        protected void SetDefaultAccounts()
        {
            var salesAccount = accountsLookup.FirstOrDefault(x => x.AccountCode == "40100");
            var expenseAccount = accountsLookup.FirstOrDefault(x => x.AccountCode == "50300");
            var inventoryAccount = accountsLookup.FirstOrDefault(x => x.AccountCode == "10800");
            
            formModel.SalesAccountId = salesAccount?.Id;
            formModel.CostOfGoodsSoldAccountId = expenseAccount?.Id;
            formModel.InventoryAccountId = inventoryAccount?.Id;
        }

        protected void SalesDropdownLoadData(LoadDataArgs args)
        {
            salesAccountDropdown = SearchAccountsDropdown(args).ToArray();
            InvokeAsync(StateHasChanged);
        }

        protected void ExpenseDropdownLoadData(LoadDataArgs args)
        {
            expenseAccountDropdown = SearchAccountsDropdown(args).ToArray();
            InvokeAsync(StateHasChanged);
        }

        protected void InventoryDropdownLoadData(LoadDataArgs args)
        {
            inventoryAccountDropdown = SearchAccountsDropdown(args).ToArray();
            InvokeAsync(StateHasChanged);
        }

        protected void ItemTaxGroupDropdownLoadData(LoadDataArgs args)
        {
            var query = itemTaxGroupLookup.AsQueryable();

            if (!string.IsNullOrEmpty(args.Filter))
            {
                query = query.Where(c => c.Name.ToLower().Contains(args.Filter.ToLower()));
            }

            itemTaxGroupDropdown =  query.ToArray();
            InvokeAsync(StateHasChanged);
        }

        protected void MeasurementsDropdownLoadData(LoadDataArgs args)
        {
            var query = measurementsLookup.AsQueryable();

            if (!string.IsNullOrEmpty(args.Filter))
            {
                query = query.Where(c => c.Description.ToLower().Contains(args.Filter.ToLower()));
            }

            measurementDropdown = query.ToArray();
            InvokeAsync(StateHasChanged);
        }

        protected void ItemCategoryDropdownLoadData(LoadDataArgs args)
        {
            var query = itemCategoriesLookup.AsQueryable();

            if (!string.IsNullOrEmpty(args.Filter))
            {
                query = query.Where(c => c.Name.ToLower().Contains(args.Filter.ToLower()));
            }

            itemCategoriesDropdown = query.ToArray();
            InvokeAsync(StateHasChanged);
        }
        protected void VendorsDropdownLoadData(LoadDataArgs args)
        {
            var query = vendorsLookup.AsQueryable();

            if (!string.IsNullOrEmpty(args.Filter))
            {
                query = query.Where(c => c.Name.ToLower().Contains(args.Filter.ToLower()));
            }

            vendorsDropdown = query.ToArray();
            InvokeAsync(StateHasChanged);
        }
        IQueryable<BaseAccount> SearchAccountsDropdown(LoadDataArgs args)
        {
            var query = accountsLookup.AsQueryable();

            if (!string.IsNullOrEmpty(args.Filter))
            {
                query = query.Where(c => c.AccountName.ToLower().Contains(args.Filter.ToLower()));
            }

            return query;
        }

    }
}
