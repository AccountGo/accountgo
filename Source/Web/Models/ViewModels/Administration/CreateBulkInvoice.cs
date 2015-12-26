//-----------------------------------------------------------------------
// <copyright file="CreateBulkInvoice.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

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
