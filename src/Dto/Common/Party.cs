using System.ComponentModel.DataAnnotations;

namespace Dto.Common
{
    public class Party : BaseDto
    {
        [PublicId("pty")]
        public override int Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? Phone { get; set; }
        public string? Fax { get; set; }
    }
}
