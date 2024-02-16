using Dto.Financial;
using Dto.Inventory;
using Dto.TaxSystem;
using Microsoft.AspNetCore.Components;
using WebBlazor.Services.Contracts;

namespace WebBlazor.Pages.Inventory
{
    public class InventoryComponentBase : SuperComponentBase
    {
        [Inject]
        public IInventoryService inventoryService { get; set; } = default!;


       

    }
}
