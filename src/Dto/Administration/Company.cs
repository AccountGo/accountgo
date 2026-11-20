using System.ComponentModel.DataAnnotations;
using Dto.Common;

namespace Dto.Administration
{
  public class Company : BaseDto
  {
    [PublicId("ws")]
    public override int Id { get; set; }
    [Required]
    [Display(Name = "Company Code")]
    public string? CompanyCode { get; set; }

    [Required]
    [Display(Name = "Name")]
    public string? Name { get; set; }

    [Required]
    [Display(Name = "Short Name")]
    public string? ShortName { get; set; }

    [Required]
    [Display(Name = "CRA")]
    public string? CRA { get; set; }

    [Display(Name = "Logo")]
    public byte[]? Logo { get; set; }
  }
}
