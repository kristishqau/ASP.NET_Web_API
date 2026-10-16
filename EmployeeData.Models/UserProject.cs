using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Models
{
    public class UserProject
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int ProjectId { get; set; }
        public Project Project { get; set; } = null!;
    }
}
