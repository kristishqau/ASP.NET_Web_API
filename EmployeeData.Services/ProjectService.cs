using EmployeeData.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;

namespace EmployeeData.Services
{
    public class ProjectService : IProjectService
    {
        private readonly AppDbContext _dbContext;
        public ProjectService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public ICollection<Project> GetProjects()
        {
            return _dbContext.Projects.OrderBy(p => p.Id).ToList();
        }
        public Project GetProject(int id)
        {
            return _dbContext.Projects.Where(p => p.Id == id).FirstOrDefault();
        }
        public Project GetProjectForUser(
            int userId,
            bool isAdministrator,
            int projectId)
        {
            return VisibleProjects(userId, isAdministrator)
                .Where(project => project.Id == projectId)
                .Select(project => new Project
                {
                    Id = project.Id,
                    Name = project.Name,
                    Description = project.Description
                })
                .SingleOrDefault();
        }

        public ICollection<Task> GetTasksByProjectForUser(
            int userId,
            bool isAdministrator,
            int projectId)
        {
            var canAccessProject = VisibleProjects(userId, isAdministrator)
                .Any(project => project.Id == projectId);
            if (!canAccessProject)
                return null;

            return _dbContext.Tasks
                .AsNoTracking()
                .Where(task => task.Project.Id == projectId)
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

        public IEnumerable<Project> GetProjectsForUser(int userId, bool isAdministrator)
        {
            return VisibleProjects(userId, isAdministrator)
                .OrderBy(project => project.Id)
                .Select(project => new Project
                {
                    Id = project.Id,
                    Name = project.Name,
                    Description = project.Description
                })
                .ToList();
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
        public bool ProjectExists(int id)
        {
            return _dbContext.Projects.Any(p => p.Id == id);
        }
        public bool HasProjectMembership(int userId, int projectId)
        {
            return _dbContext.UserProjects.Any(userProject =>
                userProject.UserId == userId
                && userProject.ProjectId == projectId);
        }

        private IQueryable<Project> VisibleProjects(int userId, bool isAdministrator)
        {
            var projects = _dbContext.Projects.AsNoTracking();
            return isAdministrator
                ? projects
                : projects.Where(project => project.UserProjects.Any(userProject =>
                    userProject.UserId == userId));
        }
    }
}
