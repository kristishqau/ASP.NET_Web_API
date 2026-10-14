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
        User GetUserForUser(int currentUserId, bool isAdministrator, int userId);
        ICollection<User> GetUsersOfATaskForUser(int currentUserId, bool isAdministrator, int taskId);
        ICollection<User> GetUsersOfAProjectForUser(int currentUserId, bool isAdministrator, int projectId);
        bool UserExists(int id);
        UserAssignmentResult CreateUser(int? projectId, int? taskId, User user, string password);
        bool SaveUser();
        string Authenticate(string username, string password);
        string GetUserRole(int userId);
        int GetCurrentUserId();
        UserAssignmentResult AddUserToTaskAndProject(int userId, int? taskId, int? projectId);
        UserAssignmentResult RemoveUserFromTaskAndProject(int userId, int? taskId, int? projectId);
        DeletionResult DeleteUser(int userId);
    }
}
