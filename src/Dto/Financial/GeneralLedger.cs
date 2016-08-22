using System.Collections.Generic;

namespace Dto.Financial
{
    public class GeneralLedger : BaseDto
    {

        public int GeneralLedgerHeaderId { get; set; }
        public int AccountId { get; set; }
        public DrOrCrSide DrCr { get; set; }
        public decimal Amount { get; set; }


        public IList<GeneralLedger> ChildGeneralLedger { get; set; }

        public GeneralLedger()
        {
            ChildGeneralLedger = new List<GeneralLedger>();
        }
        public enum DrOrCrSide
        {
            NA = 0,
            Dr = 1,
            Cr = 2
        }
    }
}
