﻿using System.Collections.Generic;
using System;
using System.ComponentModel.DataAnnotations;

namespace Dto.Sales
{
    public class SalesProposalForUpdate : BaseDto
    {
        public string No { get; set; }
        public string Description { get; set; }
        [Required(ErrorMessage = "The Start Date is required")]
        public DateTime StartDate { get; set; }
        [Required(ErrorMessage = "The Expiry Date is required")]
        public DateTime ExpiryDate { get; set; }
        [Required(ErrorMessage = "The Delivery Date is required")]
        public DateTime DeliveryDate { get; set; }
        public IList<SalesProposalLineForUpdate> SalesProposalLines { get; set; }

        public int StatusId { get; set; }
   
        public int PaymentTermId { get; set; }

        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerEmail { get; set; }

        public string? CompanyName { get; set; }
        public string? CompanyEmail { get; set; }

        public string? ReferenceNo { get; set; }
    }

    public class SalesProposalLineForUpdate : BaseDto
    {
        public int SalesProposalHeaderId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Amount { get; set; }
        public decimal Discount { get; set; }

        public int ItemId { get; set; }
        public string? ItemNo { get; set; }
        public string? ItemDescription { get; set; }

        public int MeasurementId { get; set; }
        public string? MeasurementDescription { get; set; }
    }
}
