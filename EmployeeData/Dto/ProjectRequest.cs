using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Dto
{
    public sealed class ProjectRequest
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(1000)]
        public string Description { get; set; } = string.Empty;
    }
}
