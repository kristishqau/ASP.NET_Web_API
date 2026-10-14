using EmployeeData.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace EmployeeData.Services
{
    public class TaskService : ITaskService
    {
        private readonly AppDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        public TaskService(AppDbContext dbContext, IHttpContextAccessor httpContextAccessor)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }

        public Task GetTask(int id)
        {
            return _dbContext.Tasks.Where(t => t.Id == id).FirstOrDefault();
        }

        public ICollection<Task> GetTasksForUser(int userId, bool isAdministrator)
        {
            return VisibleTasks(userId, isAdministrator)
                .OrderBy(task => task.Id)
                .Select(task => new Task
                {
                    Id = task.Id,
                    Title = task.Title,
                    Description = task.Description,
                    IsFinished = task.IsFinished
                })
                .ToList();
        }

        public Task GetTaskForUser(int userId, bool isAdministrator, int taskId)
        {
            return VisibleTasks(userId, isAdministrator)
                .Where(task => task.Id == taskId)
                .Select(task => new Task
                {
                    Id = task.Id,
                    Title = task.Title,
                    Description = task.Description,
                    IsFinished = task.IsFinished
                })
                .SingleOrDefault();
        }

        public bool CreateTask(int userId, Task task)
        {

            var userTaskEntity = _dbContext.Users.Where(u => u.Id == userId).FirstOrDefault();
            
            var userTask = new UserTask()
            {
                User = userTaskEntity,
                Task = task
            };

            _dbContext.Add(userTask);

            _dbContext.Add(task);

            return SaveTask();
        }

        public DeletionResult DeleteTask(int taskId)
        {
            var task = _dbContext.Tasks.Find(taskId);
            if (task == null)
                return DeletionResult.NotFound;

            _dbContext.Tasks.Remove(task);
            return _dbContext.SaveChanges() > 0
                ? DeletionResult.Succeeded
                : DeletionResult.SaveFailed;
        }

        public bool SaveTask()
        {
            var saved = _dbContext.SaveChanges();
            return saved > 0 ? true : false;
        }
        public string GetUserRoleById(int userId)
        {
            var user = _dbContext.Users.FirstOrDefault(u => u.Id == userId);
            return user?.Role ?? "Unknown Role";
        }
        public int GetCurrentUserId()
        {
            return Convert.ToInt32(_httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }
        public bool TaskExists(int id)
        {
            return _dbContext.Tasks.Any(t => t.Id == id);
        }
        public bool TaskTitleExists(string title, int? excludedTaskId = null)
        {
            var normalizedTitle = title.Trim().ToUpperInvariant();
            return _dbContext.Tasks.Any(task =>
                (!excludedTaskId.HasValue || task.Id != excludedTaskId.Value)
                && (task.Title ?? string.Empty).Trim().ToUpper() == normalizedTitle);
        }
        public bool HasTaskAssignment(int userId, int taskId)
        {
            return _dbContext.UserTasks.Any(userTask =>
                userTask.UserId == userId
                && userTask.TaskId == taskId);
        }

        private IQueryable<Task> VisibleTasks(int userId, bool isAdministrator)
        {
            var tasks = _dbContext.Tasks.AsNoTracking();
            return isAdministrator
                ? tasks
                : tasks.Where(task =>
                    task.UserTasks.Any(userTask => userTask.UserId == userId)
                    || task.Project.UserProjects.Any(userProject =>
                        userProject.UserId == userId));
        }
    }
}
