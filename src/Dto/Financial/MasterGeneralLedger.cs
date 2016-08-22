using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dto.Financial
{
    public class MasterGeneralLedger : BaseDto
    {
        public int? Id { get; set; }
        public int AccountId { get; set; }
        public int CurrencyId { get; set; }
        public string DocumentType { get; set; }
        public int? TransactionNo { get; set; }
        public string AccountCode { get; set; }
        public string AccountName { get; set; }
        public DateTime? Date { get; set; }
        public decimal? Debit { get; set; }
        public decimal? Credit { get; set; }

        public IList<MasterGeneralLedger> ChildMasterGeneralLedger { get; set; }
        public MasterGeneralLedger()
        {
            ChildMasterGeneralLedger = new List<MasterGeneralLedger>();
        }

        public int? GroupId { get; set; }
    }
}
