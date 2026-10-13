using EmployeeData.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace EmployeeData.Services
{
    public class UserService : IUserService
    {
        private readonly AppDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly ILogger<UserService> _logger;

        public UserService(
            AppDbContext dbContext,
            IHttpContextAccessor httpContextAccessor,
            IJwtTokenService jwtTokenService,
            IPasswordHasher<User> passwordHasher,
            ILogger<UserService> logger)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
            _jwtTokenService = jwtTokenService;
            _passwordHasher = passwordHasher;
            _logger = logger;
        }

        public ICollection<User> GetUsers()
        {
            return _dbContext.Users.OrderBy(u => u.Id).ToList();
        }
        public User GetUser(int userId)
        {
            return _dbContext.Users.Where(u => u.Id == userId).FirstOrDefault();
        }
        public ICollection<User> GetUsersOfATask(int taskId)
        {
            return _dbContext.UserTasks.Where(u => u.Task.Id == taskId).Select(u => u.User).ToList();
        }
        public ICollection<User> GetUsersOfAProject(int projectId)
        {
            return _dbContext.UserProjects.Where(p => p.Project.Id == projectId).Select(u => u.User).ToList();
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

            user.Password = string.Empty;
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
                    && EF.Property<int?>(candidate.Task, "ProjectId") == projectId.Value
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
            out EmployeeData.Models.Task task,
            out Project project)
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
                ? null
                : _dbContext.Entry(task).Property<int?>("ProjectId").CurrentValue;
            if (task != null && !taskProjectId.HasValue)
            {
                project = null;
                return UserAssignmentResult.TaskHasNoProject;
            }

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

        public bool DeleteUser(User user)
        {
            _dbContext.Remove(user);
            return SaveUser();
        }

        public int GetCurrentUserId()
        {
            return Convert.ToInt32(_httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
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
        public string Authenticate(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || password == null)
                return null;

            const int maxVerificationAttempts = 2;
            for (var attempt = 0; attempt < maxVerificationAttempts; attempt++)
            {
                var user = _dbContext.Users
                    .AsNoTracking()
                    .SingleOrDefault(candidate => candidate.UserName == username);

                if (user == null)
                    return null;

                if (user.PasswordHash == null)
                {
                    if (!LegacyPasswordMatches(user.Password, password))
                        return null;

                    var passwordHash = _passwordHasher.HashPassword(user, password);
                    var updated = _dbContext.Users
                        .Where(candidate =>
                            candidate.Id == user.Id
                            && candidate.PasswordHash == null
                            && candidate.Password == user.Password)
                        .ExecuteUpdate(setters => setters
                            .SetProperty(candidate => candidate.PasswordHash, passwordHash)
                            .SetProperty(candidate => candidate.Password, string.Empty));

                    if (updated == 0)
                        continue;

                    user.PasswordHash = passwordHash;
                    user.Password = string.Empty;
                    return _jwtTokenService.CreateToken(user);
                }

                PasswordVerificationResult verificationResult;
                try
                {
                    verificationResult = _passwordHasher.VerifyHashedPassword(
                        user,
                        user.PasswordHash,
                        password);
                }
                catch (FormatException)
                {
                    _logger.LogWarning(
                        "Stored password hash is malformed for user {UserId}.",
                        user.Id);
                    return null;
                }

                if (verificationResult == PasswordVerificationResult.Failed)
                    return null;

                var requiresRehash =
                    verificationResult == PasswordVerificationResult.SuccessRehashNeeded;
                if (!requiresRehash && string.IsNullOrEmpty(user.Password))
                    return _jwtTokenService.CreateToken(user);

                var replacementHash = requiresRehash
                    ? _passwordHasher.HashPassword(user, password)
                    : user.PasswordHash;
                var hashUpdated = _dbContext.Users
                    .Where(candidate =>
                        candidate.Id == user.Id
                        && candidate.PasswordHash == user.PasswordHash
                        && candidate.Password == user.Password)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(candidate => candidate.PasswordHash, replacementHash)
                        .SetProperty(candidate => candidate.Password, string.Empty));

                if (hashUpdated == 0)
                    continue;

                user.PasswordHash = replacementHash;
                user.Password = string.Empty;
                return _jwtTokenService.CreateToken(user);
            }

            return null;
        }

        private static bool LegacyPasswordMatches(string storedPassword, string providedPassword)
        {
            if (storedPassword == null)
                return false;

            var storedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(storedPassword));
            var providedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(providedPassword));
            return CryptographicOperations.FixedTimeEquals(storedDigest, providedDigest);
        }
    }
}
