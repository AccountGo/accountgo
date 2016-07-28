using System.ComponentModel.DataAnnotations;

namespace Dto.Common
{
    public class Party : BaseDto
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public string Phone { get; set; }
        public string Fax { get; set; }
    }
}
