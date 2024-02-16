namespace Dto.Common.Response
{
    public class GetPartyResponse : BaseDto
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public string Phone { get; set; }
        public string Fax { get; set; }
    }
}
