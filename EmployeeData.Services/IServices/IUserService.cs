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

    public enum UserChangeResult
    {
        Succeeded,
        NoChange,
        NotFound,
        LastAdministrator,
        SaveFailed
    }

    public interface IUserService
    {
        PagedResult<User> GetUsers(PageRequest page);
        User? GetUser(int userId);
        User? GetUserForUser(int currentUserId, bool isAdministrator, int userId);
        PagedResult<User>? GetUsersOfATaskForUser(
            int currentUserId,
            bool isAdministrator,
            int taskId,
            PageRequest page);
        PagedResult<User>? GetUsersOfAProjectForUser(
            int currentUserId,
            bool isAdministrator,
            int projectId,
            PageRequest page);
        bool UserExists(int id);
        bool UserNameExists(string userName, int? excludedUserId = null);
        UserAssignmentResult CreateUser(int? projectId, int? taskId, User user, string password);
        bool SaveUser();
        string GetUserRole(int userId);
        int GetCurrentUserId();
        UserChangeResult SetUserRole(int userId, string role);
        UserAssignmentResult AddUserToTaskAndProject(int userId, int? taskId, int? projectId);
        UserAssignmentResult RemoveUserFromTaskAndProject(int userId, int? taskId, int? projectId);
        UserChangeResult DeleteUser(int userId);
    }
}
