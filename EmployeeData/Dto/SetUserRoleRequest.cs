using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Dto
{
    public sealed class SetUserRoleRequest
    {
        [Required]
        [StringLength(32)]
        public string Role { get; set; } = string.Empty;
    }
}
