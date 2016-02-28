using System;
using System.ComponentModel.DataAnnotations;

namespace Web.Models.ViewModels.Administration
{
    public class FinancialYearModel : EntityModelBase
    {
        [Required]
        [StringLength(10)]
        public string FiscalYearCode { get; set; }
        [Required]
        [StringLength(100)]
        public string FiscalYearName { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; }
    }
}