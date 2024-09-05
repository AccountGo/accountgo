using System;
using System.Collections.Generic;
using System.Collections;

namespace Dto.Sales
{
    public class SalesProposal : BaseDto
    {
        public string? No { get; set; }
        public int? PaymentTermId { get; set; }
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyEmail { get; set; }
        public decimal Amount { get { return GetTotalAmount(); } }

        public string? Description { get; set; }
        public DateTime DeliveryDate { get; set; }
        public string? ReferenceNo { get; set; }
        public IList<SalesProposalLine>? SalesProposalLines { get; set; }

        private decimal GetTotalAmount()
        {
            return GetTotalAmountExcludingTax();
        }

        private decimal GetTotalAmountExcludingTax()
        {
            decimal totalAmount = 0;

            foreach(var line in SalesProposalLines)
            {
                if (line.Quantity is null || line.Amount is null) continue;

                decimal quantityXamount = line.Quantity!.Value * line.Amount!.Value;
                decimal discountPercentage = line.Discount.HasValue == true && line.Discount > 0 
                    ? (line.Discount.Value / 100) : 0;

                decimal discount = quantityXamount * discountPercentage;

                totalAmount += (quantityXamount - discount);
            }

            return totalAmount;
        }

    }

    public class SalesProposalLine : BaseDto
    {
        public decimal? Quantity { get; set; }
        public decimal? Amount { get; set; }
        public decimal? Discount { get; set; }

        public int? ItemId { get; set; }
        public string? ItemNo { get; set; }
        public string? ItemDescription { get; set; }

        public int? MeasurementId { get; set; }
        public string? MeasurementDescription { get; set; }
    }
}
