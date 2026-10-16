using EmployeeData.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;

namespace EmployeeData.Services
{
    public class UserService : IUserService
    {
        private readonly AppDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IPasswordHasher<User> _passwordHasher;

        public UserService(
            AppDbContext dbContext,
            IHttpContextAccessor httpContextAccessor,
            IPasswordHasher<User> passwordHasher)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
            _passwordHasher = passwordHasher;
        }

        public PagedResult<User> GetUsers(PageRequest page)
        {
            return _dbContext.Users
                .AsNoTracking()
                .OrderBy(user => user.Id)
                .Select(PublicUser())
                .ToPage(page);
        }
        public User? GetUser(int userId)
        {
            return _dbContext.Users.Where(u => u.Id == userId).FirstOrDefault();
        }

        public User? GetUserForUser(
            int currentUserId,
            bool isAdministrator,
            int userId)
        {
            return _dbContext.Users
                .AsNoTracking()
                .Where(user => user.Id == userId)
                .Where(user => isAdministrator || user.Id == currentUserId)
                .Select(PublicUser())
                .SingleOrDefault();
        }

        public PagedResult<User>? GetUsersOfATaskForUser(
            int currentUserId,
            bool isAdministrator,
            int taskId,
            PageRequest page)
        {
            var visibleTaskExists = _dbContext.Tasks
                .AsNoTracking()
                .Where(task => task.Id == taskId)
                .Any(task => isAdministrator
                    || task.UserTasks.Any(userTask =>
                        userTask.UserId == currentUserId)
                    || task.Project.UserProjects.Any(userProject =>
                        userProject.UserId == currentUserId));
            if (!visibleTaskExists)
                return null;

            return _dbContext.UserTasks
                .AsNoTracking()
                .Where(userTask => userTask.TaskId == taskId)
                .OrderBy(userTask => userTask.UserId)
                .Select(userTask => userTask.User)
                .Select(PublicUser())
                .ToPage(page);
        }

        public PagedResult<User>? GetUsersOfAProjectForUser(
            int currentUserId,
            bool isAdministrator,
            int projectId,
            PageRequest page)
        {
            var visibleProjectExists = _dbContext.Projects
                .AsNoTracking()
                .Where(project => project.Id == projectId)
                .Any(project => isAdministrator
                    || project.UserProjects.Any(userProject =>
                        userProject.UserId == currentUserId));
            if (!visibleProjectExists)
                return null;

            return _dbContext.UserProjects
                .AsNoTracking()
                .Where(userProject => userProject.ProjectId == projectId)
                .OrderBy(userProject => userProject.UserId)
                .Select(userProject => userProject.User)
                .Select(PublicUser())
                .ToPage(page);
        }

        public UserAssignmentResult CreateUser(int? projectId, int? taskId, User user, string password)
        {
            var assignmentResult = ResolveAssignment(
                taskId,
                projectId,
                out var task,
                out var project);
            if (assignmentResult != UserAssignmentResult.Succeeded)
                return assignmentResult;

            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            _dbContext.Users.Add(user);

            if (project != null)
            {
                _dbContext.UserProjects.Add(new UserProject
                {
                    User = user,
                    Project = project
                });
            }

            if (task != null)
            {
                _dbContext.UserTasks.Add(new UserTask
                {
                    User = user,
                    Task = task
                });
            }

            return _dbContext.SaveChanges() > 0
                ? UserAssignmentResult.Succeeded
                : UserAssignmentResult.SaveFailed;
        }

        public UserAssignmentResult AddUserToTaskAndProject(int userId, int? taskId, int? projectId)
        {
            if (!taskId.HasValue && !projectId.HasValue)
                return UserAssignmentResult.InvalidSelection;

            var user = _dbContext.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
                return UserAssignmentResult.UserNotFound;

            var assignmentResult = ResolveAssignment(
                taskId,
                projectId,
                out var task,
                out var project);
            if (assignmentResult != UserAssignmentResult.Succeeded)
                return assignmentResult;

            var hasChanges = false;
            if (project != null
                && !_dbContext.UserProjects.Any(userProject =>
                    userProject.UserId == userId
                    && userProject.ProjectId == project.Id))
            {
                _dbContext.UserProjects.Add(new UserProject
                {
                    User = user,
                    Project = project
                });
                hasChanges = true;
            }

            if (task != null
                && !_dbContext.UserTasks.Any(userTask =>
                    userTask.UserId == userId
                    && userTask.TaskId == task.Id))
            {
                _dbContext.UserTasks.Add(new UserTask
                {
                    User = user,
                    Task = task
                });
                hasChanges = true;
            }

            if (!hasChanges)
                return UserAssignmentResult.Succeeded;

            return _dbContext.SaveChanges() > 0
                ? UserAssignmentResult.Succeeded
                : UserAssignmentResult.SaveFailed;
        }

        public UserAssignmentResult RemoveUserFromTaskAndProject(int userId, int? taskId, int? projectId)
        {
            if (!taskId.HasValue && !projectId.HasValue)
                return UserAssignmentResult.InvalidSelection;

            if (!_dbContext.Users.Any(user => user.Id == userId))
                return UserAssignmentResult.UserNotFound;

            var assignmentResult = ResolveAssignment(
                taskId,
                projectId,
                out _,
                out _);
            if (assignmentResult != UserAssignmentResult.Succeeded)
                return assignmentResult;

            var userTask = taskId.HasValue
                ? _dbContext.UserTasks.FirstOrDefault(candidate =>
                    candidate.UserId == userId
                    && candidate.TaskId == taskId.Value)
                : null;
            if (taskId.HasValue && userTask == null)
                return UserAssignmentResult.UserNotAssignedToTask;

            var userProject = projectId.HasValue
                ? _dbContext.UserProjects.FirstOrDefault(candidate =>
                    candidate.UserId == userId
                    && candidate.ProjectId == projectId.Value)
                : null;
            if (projectId.HasValue && userProject == null)
                return UserAssignmentResult.UserNotMemberOfProject;

            if (projectId.HasValue)
            {
                var hasOtherTaskAssignments = _dbContext.UserTasks.Any(candidate =>
                    candidate.UserId == userId
                    && candidate.Task.ProjectId == projectId.Value
                    && (!taskId.HasValue || candidate.TaskId != taskId.Value));
                if (hasOtherTaskAssignments)
                    return UserAssignmentResult.ProjectHasTaskAssignments;
            }

            if (userTask != null)
                _dbContext.UserTasks.Remove(userTask);

            if (userProject != null)
                _dbContext.UserProjects.Remove(userProject);

            return _dbContext.SaveChanges() > 0
                ? UserAssignmentResult.Succeeded
                : UserAssignmentResult.SaveFailed;
        }

        private UserAssignmentResult ResolveAssignment(
            int? taskId,
            int? projectId,
            out EmployeeData.Models.Task? task,
            out Project? project)
        {
            task = taskId.HasValue
                ? _dbContext.Tasks.FirstOrDefault(candidate => candidate.Id == taskId.Value)
                : null;
            if (taskId.HasValue && task == null)
            {
                project = null;
                return UserAssignmentResult.TaskNotFound;
            }

            var taskProjectId = task == null
                ? (int?)null
                : task.ProjectId;

            if (taskProjectId.HasValue
                && projectId.HasValue
                && taskProjectId.Value != projectId.Value)
            {
                project = null;
                return UserAssignmentResult.TaskProjectMismatch;
            }

            var effectiveProjectId = projectId ?? taskProjectId;
            project = effectiveProjectId.HasValue
                ? _dbContext.Projects.FirstOrDefault(candidate =>
                    candidate.Id == effectiveProjectId.Value)
                : null;

            return effectiveProjectId.HasValue && project == null
                ? UserAssignmentResult.ProjectNotFound
                : UserAssignmentResult.Succeeded;
        }

        public UserChangeResult SetUserRole(int userId, string role)
        {
            using var transaction = _dbContext.Database.BeginTransaction(
                IsolationLevel.Serializable);
            var user = _dbContext.Users.SingleOrDefault(candidate => candidate.Id == userId);
            if (user == null)
                return UserChangeResult.NotFound;

            if (user.Role == role)
                return UserChangeResult.NoChange;

            if (user.Role == "Administrator"
                && role != "Administrator"
                && !_dbContext.Users.Any(candidate =>
                    candidate.Role == "Administrator"
                    && candidate.Id != userId))
            {
                return UserChangeResult.LastAdministrator;
            }

            user.Role = role;
            if (_dbContext.SaveChanges() <= 0)
                return UserChangeResult.SaveFailed;

            transaction.Commit();
            return UserChangeResult.Succeeded;
        }

        public UserChangeResult DeleteUser(int userId)
        {
            using var transaction = _dbContext.Database.BeginTransaction(
                IsolationLevel.Serializable);
            var user = _dbContext.Users.SingleOrDefault(candidate => candidate.Id == userId);
            if (user == null)
                return UserChangeResult.NotFound;

            if (user.Role == "Administrator"
                && !_dbContext.Users.Any(candidate =>
                    candidate.Role == "Administrator"
                    && candidate.Id != userId))
            {
                return UserChangeResult.LastAdministrator;
            }

            _dbContext.Users.Remove(user);
            if (_dbContext.SaveChanges() <= 0)
                return UserChangeResult.SaveFailed;

            transaction.Commit();
            return UserChangeResult.Succeeded;
        }

        public int GetCurrentUserId()
        {
            var userIdValue = _httpContextAccessor.HttpContext?
                .User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdValue, out var userId))
                throw new InvalidOperationException(
                    "The authenticated user identifier is unavailable.");

            return userId;
        }
        public string GetUserRole(int userId)
        {
            var user = _dbContext.Users.FirstOrDefault(u => u.Id == userId);
            if (user != null)
            {
                return user.Role;
            }
            return "Unknown Role";
        }
        public bool SaveUser()
        {
            var saved = _dbContext.SaveChanges();
            return saved > 0 ? true : false;
        }
        public bool UserExists(int id)
        {
            return _dbContext.Users.Any(u => u.Id == id);
        }
        public bool UserNameExists(string userName, int? excludedUserId = null)
        {
            var normalizedUserName = userName.Trim().ToUpperInvariant();
            return _dbContext.Users.Any(user =>
                (!excludedUserId.HasValue || user.Id != excludedUserId.Value)
                && (user.UserName ?? string.Empty).Trim().ToUpper() == normalizedUserName);
        }
        private static Expression<Func<User, User>> PublicUser()
        {
            return user => new User
            {
                Id = user.Id,
                UserName = user.UserName,
                Role = user.Role,
                ProfilePictureUrl = user.ProfilePictureUrl
            };
        }
    }
}
