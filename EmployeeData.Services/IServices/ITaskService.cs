using EmployeeData.Models;
using System.Collections.Generic;

namespace EmployeeData.Services
{
    public interface ITaskService
    {
        ICollection<Task> GetTasks(); 
        Task GetTask(int id); 
        bool TaskExists(int id);
        ICollection<Task> GetTasksByUser(int userId);
        bool CreateTask(int userId, Task task);
        bool SaveTask();
        string GetUserRoleById(int userId);
        bool UpdateTask(Task task);
        bool IsUserInTask(int userId, int taskId);
        bool DeleteTask(Task task);
        int GetCurrentUserId();
    }
}
