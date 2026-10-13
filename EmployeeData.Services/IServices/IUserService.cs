using EmployeeData.Models;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace EmployeeData.Services
{
    public enum UserAssignmentResult
    {
        Succeeded,
        InvalidSelection,
        UserNotFound,
        TaskNotFound,
        ProjectNotFound,
        TaskHasNoProject,
        TaskProjectMismatch,
        UserNotAssignedToTask,
        UserNotMemberOfProject,
        ProjectHasTaskAssignments,
        SaveFailed
    }

    public interface IUserService
    {
        ICollection<User> GetUsers();
        User GetUser(int userId);
        ICollection<User> GetUsersOfATask(int taskId);
        ICollection<User> GetUsersOfAProject(int projectId);
        bool UserExists(int id);
        UserAssignmentResult CreateUser(int? projectId, int? taskId, User user, string password);
        bool SaveUser();
        string Authenticate(string username, string password);
        string GetUserRole(int userId);
        int GetCurrentUserId();
        UserAssignmentResult AddUserToTaskAndProject(int userId, int? taskId, int? projectId);
        UserAssignmentResult RemoveUserFromTaskAndProject(int userId, int? taskId, int? projectId);
        bool DeleteUser(User user);
    }
}
