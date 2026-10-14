using EmployeeData.Models;
using System.Collections.Generic;

namespace EmployeeData.Services
{
    public interface ITaskService
    {
        ICollection<Task> GetTasks(); 
        Task GetTask(int id); 
        ICollection<Task> GetTasksForUser(int userId, bool isAdministrator);
        Task GetTaskForUser(int userId, bool isAdministrator, int taskId);
        bool TaskExists(int id);
        bool CreateTask(int userId, Task task);
        bool SaveTask();
        string GetUserRoleById(int userId);
        bool HasTaskAssignment(int userId, int taskId);
        DeletionResult DeleteTask(int taskId);
        int GetCurrentUserId();
    }
}
