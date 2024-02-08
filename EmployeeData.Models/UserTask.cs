using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Models
{
    public class UserTask
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; }

        public int TaskId { get; set; }
        public Task Task { get; set; }
    }
}
