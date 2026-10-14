using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Models
{
    public class Task
    {
        [Key]
        public int Id { get; set; }
        [Required]
        [StringLength(200)]
        public string Title { get; set; }
        [StringLength(2000)]
        public string Description { get; set; }
        public bool IsFinished { get; set; }
        public int ProjectId { get; set; }
        [Required]
        public Project Project { get; set; }
        public ICollection<UserTask> UserTasks { get; set; }
    }
}
