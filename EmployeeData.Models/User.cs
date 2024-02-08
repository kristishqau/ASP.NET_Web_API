using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }
        [Required]
        public string UserName { get; set; }
        [Required]
        public string Password { get; set; }
        public string Role { get; set; }
        public string ProfilePictureUrl { get; set; }
        public ICollection<UserProject> UserProjects { get; set; }
        public ICollection<UserTask> UserTasks { get; set; }
    }
}
