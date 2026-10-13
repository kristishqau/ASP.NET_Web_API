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
        private readonly IProjectService _projectService;
        private readonly ITaskService _taskService;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly ILogger<UserService> _logger;

        public UserService(
            AppDbContext dbContext,
            IProjectService projectService,
            ITaskService taskService,
            IHttpContextAccessor httpContextAccessor,
            IJwtTokenService jwtTokenService,
            IPasswordHasher<User> passwordHasher,
            ILogger<UserService> logger)
        {
            _dbContext = dbContext;
            _projectService = projectService;
            _httpContextAccessor = httpContextAccessor;
            _taskService = taskService;
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

        public bool CreateUser(int? projectId, int? taskId, User user, string password)
        {
            user.Password = string.Empty;
            user.PasswordHash = _passwordHasher.HashPassword(user, password);

            if (projectId.HasValue)
            {
                var userProjectEntity = _dbContext.Projects.Where(p => p.Id == projectId).FirstOrDefault();

                if (userProjectEntity != null)
                {
                    var userProject = new UserProject()
                    {
                        User = user,
                        Project = userProjectEntity
                    };

                    _dbContext.Add(userProject);
                }
            }

            if (taskId.HasValue)
            {

                var task = _dbContext.Tasks.Where(t => t.Id == taskId).FirstOrDefault();

                if (task != null)
                {

                    var userTask = new UserTask()
                    {
                        User = user,
                        Task = task
                    };

                    _dbContext.Add(userTask);
                }
            }

            _dbContext.Add(user);

            return SaveUser();
        }
        public bool AddUserToTaskAndProject(int userId, int? taskId, int? projectId)
        {
            if (_dbContext.UserTasks.Any(ut => ut.UserId == userId && ut.TaskId == taskId) && _dbContext.UserProjects.Any(up => up.UserId == userId && up.ProjectId == projectId))
            {
                return false;
            }

            var user = _dbContext.Users.FirstOrDefault(u => u.Id == userId);
            var task = _dbContext.Tasks.FirstOrDefault(t => t.Id == taskId);
            var project = _dbContext.Projects.FirstOrDefault(p => p.Id == projectId);

            if (user != null && task != null && project != null)
            {
                if (GetUserRole(GetCurrentUserId()) == "Employee" && _projectService.IsUserInProject(GetCurrentUserId(), projectId.GetValueOrDefault()))
                {
                    if (!_projectService.IsUserInProject(userId, projectId.GetValueOrDefault()))
                    {
                        return false;
                    }
                }
                if (!_dbContext.UserTasks.Any(ut => ut.UserId == userId && ut.TaskId == taskId))
                {
                    var userTask = new UserTask
                    {
                        User = user,
                        Task = task
                    };
                    _dbContext.UserTasks.Add(userTask);
                    _dbContext.SaveChanges();
                }

                if (!_dbContext.UserProjects.Any(up => up.UserId == userId && up.ProjectId == projectId))
                {
                    var userProject = new UserProject
                    {
                        User = user,
                        Project = project
                    };
                    _dbContext.UserProjects.Add(userProject);
                    _dbContext.SaveChanges();
                }
                _dbContext.SaveChanges();
                return true;
            }

            return false;
        }

        public bool RemoveUserFromTaskAndProject(int userId, int? taskId, int? projectId)
        {
            if (taskId.HasValue)
            {
                var userTask = _dbContext.UserTasks.FirstOrDefault(ut => ut.UserId == userId && ut.TaskId == taskId);
                if (userTask != null)
                {
                    _dbContext.UserTasks.Remove(userTask);
                }
            }

            if (projectId.HasValue)
            {
                var userProject = _dbContext.UserProjects.FirstOrDefault(up => up.UserId == userId && up.ProjectId == projectId);
                if (userProject != null)
                {
                    _dbContext.UserProjects.Remove(userProject);
                }
            }

            if (taskId.HasValue || projectId.HasValue)
            {
                _dbContext.SaveChanges();
                return true;
            }
            return false;
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
