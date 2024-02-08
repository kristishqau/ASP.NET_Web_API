using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Models
{
    public class Task
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string Title { get; set; }
        public string Description { get; set; }
        public bool IsFinished { get; set; }
        public Project Project { get; set; }
        public ICollection<UserTask> UserTasks { get; set; }
    }
}
