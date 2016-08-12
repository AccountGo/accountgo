namespace Dto.Administration
{
    public class Company : BaseDto
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string Name { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public string ShortName { get; set; }
        [System.ComponentModel.DataAnnotations.Required]
        public string CompanyCode { get; set; }
    }
}
