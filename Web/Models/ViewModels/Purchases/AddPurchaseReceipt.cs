using System;
using System.Linq;
using System.Collections.Generic;

namespace Web.Models.ViewModels.Purchases
{
    public class AddPurchaseReceipt
    {
        public AddPurchaseReceipt()
        {
            PurchaseReceiptLines = new List<AddPurchaseReceiptLine>();
        }

        public int Id { get; set; }
        public string No { get; set; }
        public DateTime Date { get; set; }
        public string Vendor { get; set; }
        public decimal Amount { get; set; }

        public IList<AddPurchaseReceiptLine> PurchaseReceiptLines { get; set; }

        public void PreparePurchaseReceiptViewModel(Core.Domain.Purchases.PurchaseOrderHeader po)
        {
            Id = po.Id;
            No = po.No;
            Date = po.Date;
            Vendor = po.Vendor.Name;
            Amount = po.PurchaseOrderLines.Sum(a => a.Amount);
            foreach(var line in po.PurchaseOrderLines)
            {
                PurchaseReceiptLines.Add(new AddPurchaseReceiptLine()
                {
                    Id = line.Id,
                    ItemId = line.ItemId,
                    PurchaseOrderLineId = line.Id,
                    UnitOfMeasurementId = line.MeasurementId,
                    Quantity = line.Quantity,
                    Cost = line.Cost,
                    ReceiptQuantity = line.GetReceivedQuantity(),
                    IsCompleted = line.IsCompleted()
                });
            }
        }
    }

    public class AddPurchaseReceiptLine
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public int? PurchaseOrderLineId { get; set; }
        public string Description { get; set; }
        public int UnitOfMeasurementId { get; set; }
        public decimal Quantity { get; set; }
        public decimal? Cost { get; set; }
        public decimal TotalLineCost { get; set; }
        public decimal? ReceiptQuantity { get; set; }
        public decimal? InQty { get; set; }
        public bool IsCompleted { get; set; }
    }
}