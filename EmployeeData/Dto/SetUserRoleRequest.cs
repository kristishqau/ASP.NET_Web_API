using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Dto
{
    public sealed class SetUserRoleRequest
    {
        [Required]
        public string Role { get; set; } = string.Empty;
    }
}
