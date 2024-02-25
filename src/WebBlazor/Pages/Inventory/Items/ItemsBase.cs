using Dto.Financial;
using Dto.Inventory.Response;
using Microsoft.AspNetCore.Components;
using Radzen.Blazor;
using System.Security.AccessControl;
using WebBlazor.Enums;
using WebBlazor.Services;
using WebBlazor.Services.Contracts;

namespace WebBlazor.Pages.Inventory.Items
{
    public class ItemsBase : ComponentBase
    {

        [Inject]
        public IInventoryService inventoryService { get; set; } = default!;
        [Inject]
        public NavigationManager navigationManager { get; set; } = default!;
        protected RadzenDataGrid<GetItem> grid { get; set; } = default!;
        protected IEnumerable<GetItem> items = default!;
        protected bool isLoading = false;

        protected override async Task OnInitializedAsync()
        {
            isLoading = true;
            var response = await inventoryService.GetItems();
            items = response;
            isLoading = false;
        }

        protected void GotoCreateItem()
        {
            Console.WriteLine("Goto Create item");
            navigationManager.NavigateTo(PageRoutes.Inventory.CreateItem);
        }
    }
}
