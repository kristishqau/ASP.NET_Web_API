using EmployeeData.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;

namespace EmployeeData.Services
{
    public class UserService : IUserService
    {
        private readonly AppDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IProjectService _projectService;
        private readonly ITaskService _taskService;

        public UserService(AppDbContext dbContext, IProjectService projectService, ITaskService taskService, IHttpContextAccessor httpContextAccessor)
        {
            _dbContext = dbContext;
            _projectService = projectService;
            _httpContextAccessor = httpContextAccessor;
            _taskService = taskService;
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

        public bool CreateUser(int? projectId, int? taskId, User user)
        {
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
        public bool UpdateUser( User user)
        {
            _dbContext.Update(user);
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
            var user = _dbContext.Users.SingleOrDefault(u => u.UserName == username && u.Password == password);

            if (user == null)
            {
                return null;
            }
            var token = GenerateJwtToken(user);
            return token;
        }
        private static string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes("qP4tShvWJswe5lZvGv9N3dmKw1Lr2NxzUV8y7iA3Xr6wM0aT5gB1pO3iXu8cR0f");

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim(ClaimTypes.Role, user.Role),
                    new Claim("id", user.Id.ToString())
                }),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
            };
            try
            {
                var token = tokenHandler.CreateToken(tokenDescriptor);
                var writtenToken = tokenHandler.WriteToken(token);
                Console.WriteLine($"Generated JWT token: {writtenToken}");
                return writtenToken;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating JWT token: {ex.Message}");
                throw;
            }
        }
    }
}
