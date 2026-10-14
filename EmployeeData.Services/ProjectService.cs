using EmployeeData.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Data;
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

        public DeletionResult DeleteProject(int projectId)
        {
            using var transaction = _dbContext.Database.BeginTransaction(
                IsolationLevel.Serializable);
            var project = _dbContext.Projects
                .Include(candidate => candidate.Tasks)
                .SingleOrDefault(candidate => candidate.Id == projectId);
            if (project == null)
                return DeletionResult.NotFound;

            if (project.Tasks.Any(task => !task.IsFinished))
                return DeletionResult.HasUnfinishedTasks;

            _dbContext.Tasks.RemoveRange(project.Tasks);
            _dbContext.Projects.Remove(project);

            if (_dbContext.SaveChanges() <= 0)
                return DeletionResult.SaveFailed;

            transaction.Commit();
            return DeletionResult.Succeeded;
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
