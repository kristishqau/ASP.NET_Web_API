using EmployeeData.Models;
using System.Collections.Generic;

namespace EmployeeData.Services
{
    public interface IProjectService
    {
        ICollection<Project> GetProjects(); 
        Project GetProject(int id); 
        Project GetProjectForUser(int userId, bool isAdministrator, int projectId);
        ICollection<Task> GetTasksByProjectForUser(int userId, bool isAdministrator, int projectId);
        bool ProjectExists(int id);
        bool HasProjectMembership(int userId, int projectId);
        IEnumerable<Project> GetProjectsForUser(int userId, bool isAdministrator);
        bool CreateProject(int userId, Project project);
        bool SaveProject();
        DeletionResult DeleteProject(int projectId);
    }
}
