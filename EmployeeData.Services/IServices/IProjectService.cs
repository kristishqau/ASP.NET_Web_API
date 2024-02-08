using EmployeeData.Models;
using System.Collections.Generic;

namespace EmployeeData.Services
{
    public interface IProjectService
    {
        ICollection<Project> GetProjects(); 
        Project GetProject(int id); 
        ICollection<Task> GetTasksByProject(int projectId);
        bool ProjectExists(int id);
        bool IsUserInProject(int userId, int projectId);
        IEnumerable<Project> GetProjectsForUser(int userId);
        bool CreateProject(int userId, Project project);
        bool SaveProject();
        bool UpdateProject(Project project);
        bool DeleteProject(Project project);
        IEnumerable<int> GetTaskIdsByProjectId(int projectId);
        bool IsTaskFinished(int taskId);
    }
}
