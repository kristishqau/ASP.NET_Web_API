using EmployeeData.Security;
using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Dto
{
    public sealed class CreateUserRequest
    {
        [Required]
        [StringLength(100)]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [StringLength(
            PasswordPolicy.MaximumLength,
            MinimumLength = PasswordPolicy.MinimumLength)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [StringLength(32)]
        public string Role { get; set; } = string.Empty;

        [StringLength(2048)]
        public string? ProfilePictureUrl { get; set; }
    }
}
