using EmployeeData.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace EmployeeData.Services
{
    public class ProjectService : IProjectService
    {
        private readonly AppDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ProjectService(AppDbContext dbContext,IHttpContextAccessor httpContextAccessor)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }

        public ICollection<Project> GetProjects()
        {
            return _dbContext.Projects.OrderBy(p => p.Id).ToList();
        }
        public Project GetProject(int id)
        {
            return _dbContext.Projects.Where(p => p.Id == id).FirstOrDefault();
        }
        public ICollection<Task> GetTasksByProject(int projectId)
        {
            var userId = GetCurrentUserId();
            var canAccessTasks = IsUserInProject(userId, projectId);
            if (!canAccessTasks)
            {
                return null; 
            }
            return _dbContext.Projects.Where(p => p.Id == projectId).SelectMany(t=>t.Tasks).ToList();
        }

        public bool CreateProject(int userId, Project project)
        {
            var userProjectEntity = _dbContext.Users.Where(u => u.Id == userId).FirstOrDefault();

            var userProject = new UserProject()
            {
                User = userProjectEntity,
                Project = project
            };

            _dbContext.Add(userProject);

            _dbContext.Add(project);

            return SaveProject();
        }

        public IEnumerable<Project> GetProjectsForUser(int userId)
        {
            var currentUserRole = GetUserRole(userId);

            if (currentUserRole == "Administrator")
            {
                return _dbContext.Projects.ToList();
            }
            else if (currentUserRole == "Employee")
            {
                var user = _dbContext.Users
            .Include(u => u.UserProjects)
            .ThenInclude(up => up.Project)
            .FirstOrDefault(u => u.Id == userId);

                if (user != null)
                {
                    return user.UserProjects.Select(up => up.Project).ToList();
                }
            }

            return Enumerable.Empty<Project>();
        }

        public bool UpdateProject(Project project)
        {
            _dbContext.Update(project);
            return SaveProject();
        }

        public bool DeleteProject(Project project)
        {
        //    if (project.Tasks.Any(task => !task.IsFinished))
        //    {
        //        return false;
        //    }

        //    var tasksToDelete = project.Tasks.ToList();

        //    foreach (var task in tasksToDelete)
        //    {
        //        task.Project = null;
        //    }

        //    _dbContext.Tasks.RemoveRange(tasksToDelete);
        //    _dbContext.SaveChanges();

            _dbContext.Remove(project);
            return SaveProject();
        }


        public IEnumerable<int> GetTaskIdsByProjectId(int projectId)
        {
            return _dbContext.Tasks
                .Where(task => task.Project.Id == projectId)
                .Select(task => task.Id)
                .ToList();
        }
        public bool IsTaskFinished(int taskId)
        {
            var task = _dbContext.Tasks.Find(taskId);
            return task != null && task.IsFinished;
        }
        public bool SaveProject()
        {
            var saved = _dbContext.SaveChanges();
            return saved > 0 ? true : false;
        }
        public int GetCurrentUserId()
        {
            return Convert.ToInt32(_httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }
        public string GetUserRole(int? userId)
        {
            var user = _dbContext.Users.FirstOrDefault(u => u.Id == userId);
            if (user != null)
            {
                return user.Role;
            }
            return "Unknown";
        }
        public bool ProjectExists(int id)
        {
            return _dbContext.Projects.Any(p => p.Id == id);
        }
        public bool IsUserInProject(int userId, int projectId)
        {
            var currentUserRole = GetUserRole(userId);
            if (currentUserRole=="Administrator")
            {
                return true;
            }
            return _dbContext.UserProjects.Any(up => up.UserId == userId && up.ProjectId == projectId);
        }
    }
}