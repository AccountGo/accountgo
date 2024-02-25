using Dto.Inventory.Response;
using Dto.Purchasing.Request;
using Dto.Purchasing.Response;
using Dto.TaxSystem;
using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;
using System.Threading.Tasks;

namespace WebBlazor.Pages.Vendor.PurchaseOrder.CreatePurchaseOrder
{
    public class CreatePurchaseOrderBase : SuperComponentBase
    {
        protected RadzenDataGrid<CreatePOLine> poLineGrid { get; set; } = default!;
        protected List<CreatePOLine> poLineToInsert { get; set; } = new();
        protected List<CreatePOLine> poLineToUpdate { get; set; } = new();
        protected CreatePO formModel { get; set; } = new();


        protected async Task Submit()
        {
        }

        protected async Task InsertRow()
        {
            var poLine = new CreatePOLine();
            poLine.Quantity = 1;
            poLineToInsert.Add(poLine);
            await poLineGrid.InsertRow(poLine);
        }

        protected void Reset()
        {
            poLineToInsert.Clear();
            poLineToUpdate.Clear();
        }

        protected void Reset(CreatePOLine order)
        {

        }


        protected async Task EditRow(CreatePOLine order)
        {
            poLineToUpdate.Add(order);
            await poLineGrid.EditRow(order);

        }

        protected void OnUpdateRow(CreatePOLine order)
        {
            Reset(order);
        }

        protected async Task SaveRow(CreatePOLine order)
        {
            await poLineGrid.UpdateRow(order);
        }

        protected void CancelEdit(CreatePOLine order)
        {
            Reset(order);

            poLineGrid.CancelEditRow(order);
        }

        protected async Task DeleteRow(CreatePOLine order)
        {
            Reset(order);
        }

        protected void OnCreateRow(CreatePOLine order)
        {
            poLineToInsert.Remove(order);
        }

    }
}
