using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Dto
{
    public sealed class UpdateUserProfileRequest
    {
        [Required]
        [StringLength(100)]
        public string UserName { get; set; } = string.Empty;

        [StringLength(2048)]
        public string? ProfilePictureUrl { get; set; }
    }
}
