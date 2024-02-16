using System.ComponentModel.DataAnnotations;

namespace Dto.Common.Response
{
    public class GetContactResponse : BaseDto
    {
        public int CompanyId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public Party Party { get; set; }

        public string MiddleName { get; set; }

        public int CustomerId { get; set; }
        public int VendorId { get; set; }
        public int HoldingPartyType { get; set; } //this is 1 or 2     Customer = 1,Vendor = 2,
        public int HoldingPartyId { get; set; } // id for customerId or VendorId    

        public GetContactResponse()
        {
            Party = new Party();
        }
    }
}
