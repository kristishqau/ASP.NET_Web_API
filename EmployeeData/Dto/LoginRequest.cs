using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Dto
{
    public sealed class LoginRequest
    {
        [Required]
        [StringLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(128)]
        public string Password { get; set; } = string.Empty;
    }
}
