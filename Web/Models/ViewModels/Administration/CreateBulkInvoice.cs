using System;
using System.ComponentModel.DataAnnotations;

namespace Web.Models.ViewModels.Administration
{
    public class CreateBulkInvoice
    {
        public CreateBulkInvoice()
        {
            InvoiceDate = DateTime.Now;
            DueDate = DateTime.Now;
        }
        [DataType(DataType.Date)]
        public DateTime InvoiceDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime DueDate { get; set; }

        public decimal TotalAmountPerInvoice { get; set; }
    }
}