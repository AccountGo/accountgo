#nullable enable
using System.ComponentModel.DataAnnotations.Schema;
using Core.Domain.Financials;

namespace Core.Domain
{
    [Table("DocumentNumberSeries")]
    public class DocumentNumberSeries : BaseEntity, ICompanyOwned
    {
        public int CompanyId { get; set; }
        public SequenceNumberTypes SequenceNumberType { get; set; }
        public int FinancialYearId { get; set; }
        public int NextNumber { get; set; } = 1;

        public virtual FinancialYear FinancialYear { get; set; } = null!;
    }
}
