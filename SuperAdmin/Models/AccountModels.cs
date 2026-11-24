using System.ComponentModel.DataAnnotations;

namespace SuperAdmin.Models;

public sealed class LoginModel
{
    [Required(ErrorMessage = "Enter your user name."), StringLength(50)]
    [Display(Name = "User name")]
    public string UserName { get; set; } = "";

    [Required(ErrorMessage = "Enter your password."), StringLength(100)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public sealed class SetupModel
{
    public bool KeyConfigured { get; set; }

    [Required(ErrorMessage = "Enter the setup key.")]
    [Display(Name = "Setup key")]
    public string? SetupKey { get; set; }

    [Required(ErrorMessage = "Choose a user name."), StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Use letters, numbers, dot, dash or underscore.")]
    [Display(Name = "User name")]
    public string UserName { get; set; } = "";

    [Required(ErrorMessage = "Enter your name."), StringLength(100)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    [EmailAddress, StringLength(150)]
    public string? Email { get; set; }

    [StringLength(15)]
    [RegularExpression(@"^[0-9+ ]*$", ErrorMessage = "Numbers only.")]
    public string? Mobile { get; set; }

    [Required(ErrorMessage = "Choose a password."), StringLength(100, MinimumLength = 10, ErrorMessage = "At least 10 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Type the password again.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password again")]
    public string ConfirmPassword { get; set; } = "";
}
