using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Models
{
    public class Project
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public ICollection<UserProject> UserProjects { get; set; }
        public ICollection<Task> Tasks { get; set; }
    }
}
