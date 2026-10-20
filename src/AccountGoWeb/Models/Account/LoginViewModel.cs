using System.ComponentModel.DataAnnotations;

namespace AccountGoWeb.Models.Account;

public class LoginViewModel
{
    [Required]
    [EmailAddress]
    public string? Email { get; set; }

    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Display(Name = "Remember me?")]
    public bool RememberMe { get; set; }
}

public class SendOtpViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class VerifyOtpViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Otp { get; set; } = string.Empty;
}
