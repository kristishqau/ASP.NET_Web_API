using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Dto
{
    public sealed class CreateUserRequest
    {
        [Required]
        [StringLength(100)]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty;

        [StringLength(2048)]
        public string ProfilePictureUrl { get; set; }
    }
}
