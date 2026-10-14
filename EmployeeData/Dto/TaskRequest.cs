using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Dto
{
    public sealed class TaskRequest
    {
        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        public bool IsFinished { get; set; }
    }
}
