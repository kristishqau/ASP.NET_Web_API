using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }
        [Required]
        [StringLength(100)]
        public string UserName { get; set; }
        [Required]
        [StringLength(512)]
        public string PasswordHash { get; set; }
        [Required]
        [StringLength(32)]
        public string Role { get; set; }
        [StringLength(2048)]
        public string ProfilePictureUrl { get; set; }
        public ICollection<UserProject> UserProjects { get; set; }
        public ICollection<UserTask> UserTasks { get; set; }
    }
}
