using EmployeeData.Models;
using System.Collections.Generic;

namespace EmployeeData.Services
{
    public interface IProjectService
    {
        Project? GetProject(int id);
        Project? GetProjectForUser(int userId, bool isAdministrator, int projectId);
        PagedResult<Task>? GetTasksByProjectForUser(
            int userId,
            bool isAdministrator,
            int projectId,
            PageRequest page);
        bool ProjectExists(int id);
        bool ProjectNameExists(string name, int? excludedProjectId = null);
        bool HasProjectMembership(int userId, int projectId);
        PagedResult<Project> GetProjectsForUser(
            int userId,
            bool isAdministrator,
            PageRequest page);
        bool CreateProject(int userId, Project project);
        bool SaveProject();
        DeletionResult DeleteProject(int projectId);
    }
}
