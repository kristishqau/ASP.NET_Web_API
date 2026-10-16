using EmployeeData.Models;
using System.Collections.Generic;

namespace EmployeeData.Services
{
    public interface ITaskService
    {
        Task? GetTask(int id);
        PagedResult<Task> GetTasksForUser(
            int userId,
            bool isAdministrator,
            PageRequest page);
        Task? GetTaskForUser(int userId, bool isAdministrator, int taskId);
        bool TaskExists(int id);
        bool TaskTitleExists(string title, int? excludedTaskId = null);
        bool CreateTask(int userId, Task task);
        bool SaveTask();
        string GetUserRoleById(int userId);
        bool HasTaskAssignment(int userId, int taskId);
        DeletionResult DeleteTask(int taskId);
        int GetCurrentUserId();
    }
}
