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

        public ICollection<Task> GetTasks()
        {
            var userId = GetCurrentUserId();
            var userRole = GetUserRoleById(userId);
            if (userRole == "Employee")
            {
                return GetTasksByUser(userId);
            }
            return _dbContext.Tasks.ToList();
        }
        public Task GetTask(int id)
        {
            return _dbContext.Tasks.Where(t => t.Id == id).FirstOrDefault();
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

        public bool UpdateTask(Task task)
        {
            _dbContext.Update(task);
            return SaveTask();
        }

        public bool DeleteTask(Task task)
        {
            _dbContext.Remove(task);
            return SaveTask();
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
        public ICollection<Task> GetTasksByUser(int userId)
        {
            return _dbContext.UserTasks.Where(t => t.UserId == userId).Select(t => t.Task).ToList();
        }
        public bool IsUserInTask(int userId, int taskId)
        {
            var currentUserRole = GetUserRoleById(userId);
            if (currentUserRole == "Administrator")
            {
                return true;
            }
            return _dbContext.UserTasks.Any(up => up.UserId == userId && up.TaskId == taskId);
        }
    }
}