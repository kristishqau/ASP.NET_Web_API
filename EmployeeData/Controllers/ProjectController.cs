using EmployeeData.Helper;
using EmployeeData.Dto;
using EmployeeData.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace EmployeeData.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProjectController : ControllerBase
    {
        private const string AdministratorRole = "Administrator";

        private readonly IProjectService _projectService;
        private readonly IUserService _userService;

        public ProjectController(
            IProjectService projectService,
            IUserService userService)
        {
            _projectService = projectService;
            _userService = userService;
        }

        [HttpGet]
        [ProducesResponseType(200, Type = typeof(IEnumerable<ProjectDto>))]
        public IActionResult GetProjects()
        {
            var projects = _projectService
                .GetProjectsForUser(GetCurrentUserId(), User.IsInRole(AdministratorRole))
                .Select(project => project.ToDto())
                .ToList();
            return Ok(projects);
        }
        [HttpGet("{id}", Name = "GetProjectById")]
        [ProducesResponseType(200, Type = typeof(ProjectDto))]
        [ProducesResponseType(404)]
        public IActionResult GetProject(int id)
        {
            var project = _projectService.GetProjectForUser(
                GetCurrentUserId(),
                User.IsInRole(AdministratorRole),
                id);
            if (project == null)
                return NotFound();

            return Ok(project.ToDto());
        }
        [HttpGet("{projectId}/tasks", Name = "GetTasksByProject")]
        [ProducesResponseType(200, Type = typeof(IEnumerable<TaskDto>))]
        [ProducesResponseType(404)]
        public IActionResult GetTasksByProject(int projectId)
        {
            var projectTasks = _projectService.GetTasksByProjectForUser(
                GetCurrentUserId(),
                User.IsInRole(AdministratorRole),
                projectId);
            if (projectTasks == null)
                return NotFound();

            var tasks = projectTasks.Select(task => task.ToDto()).ToList();
            return Ok(tasks);
        }

        [HttpPost]
        [Authorize(Roles = "Administrator")]
        [ProducesResponseType(201, Type = typeof(ProjectDto))]
        [ProducesResponseType(400)]
        [ProducesResponseType(422)]
        public IActionResult CreateProject(
            [FromQuery] int userId,
            [FromBody] ProjectRequest projectCreate)
        {
            if (!_userService.UserExists(userId))
            {
                ModelState.AddModelError(nameof(userId), "The specified user does not exist.");
                return ValidationProblem(ModelState);
            }

            projectCreate.Name = projectCreate.Name.Trim();
            projectCreate.Description = projectCreate.Description?.Trim() ?? string.Empty;
            if (projectCreate.Name.Length == 0)
            {
                ModelState.AddModelError(nameof(projectCreate.Name), "Project name cannot be blank.");
                return ValidationProblem(ModelState);
            }

            var projects = _projectService.GetProjects()
                .Where(project => string.Equals(
                    (project.Name ?? string.Empty).Trim(),
                    projectCreate.Name,
                    StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (projects != null)
            {
                ModelState.AddModelError("", "Project already exists");
                return ValidationProblem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    modelStateDictionary: ModelState);
            }

            var projectMap = projectCreate.ToEntity();

            if (!_projectService.CreateProject(userId, projectMap))
            {
                ModelState.AddModelError("", "Something went wrong while saving");
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "The request could not be completed.");
            }

            return CreatedAtRoute(
                "GetProjectById",
                new { id = projectMap.Id },
                projectMap.ToDto());
        }

        [HttpPut("{projectId}")]
        [Authorize(Roles = "Administrator")]
        [ProducesResponseType(400)]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        [ProducesResponseType(422)]
        public IActionResult UpdateProject(
            int projectId,
            [FromBody] ProjectRequest updatedProject)
        {
            var project = _projectService.GetProject(projectId);
            if (project == null)
                return NotFound();

            updatedProject.Name = updatedProject.Name.Trim();
            updatedProject.Description = updatedProject.Description?.Trim() ?? string.Empty;
            if (updatedProject.Name.Length == 0)
            {
                ModelState.AddModelError(nameof(updatedProject.Name), "Project name cannot be blank.");
                return ValidationProblem(ModelState);
            }

            if (_projectService.GetProjects().Any(project =>
                project.Id != projectId
                && string.Equals(
                    (project.Name ?? string.Empty).Trim(),
                    updatedProject.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError(nameof(updatedProject.Name), "Project already exists.");
                return ValidationProblem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    modelStateDictionary: ModelState);
            }

            if (project.Name == updatedProject.Name
                && project.Description == updatedProject.Description)
            {
                return NoContent();
            }

            project.Name = updatedProject.Name;
            project.Description = updatedProject.Description;

            if (!_projectService.SaveProject())
            {
                ModelState.AddModelError("", "Something went wrong updating the project.");
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "The request could not be completed.");
            }

            return NoContent();
        }

        [HttpGet("GetProjectsForUser")]
        public IActionResult GetProjectsForUser()
        {
            var projects = _projectService
                .GetProjectsForUser(GetCurrentUserId(), User.IsInRole(AdministratorRole))
                .Select(project => project.ToDto())
                .ToList();

            return Ok(projects);
        }

        [HttpDelete("{projectId}")]
        [Authorize(Roles = "Administrator")]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public IActionResult DeleteProject(int projectId)
        {
            var result = _projectService.DeleteProject(projectId);
            if (result == DeletionResult.NotFound)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Project not found");
            }

            if (result == DeletionResult.HasUnfinishedTasks)
            {
                ModelState.AddModelError("", "Cannot delete project with unfinished tasks.");
                return ValidationProblem(ModelState);
            }

            if (result != DeletionResult.Succeeded)
            {
                ModelState.AddModelError("", "Failed to delete project.");
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "The request could not be completed.");
            }

            return NoContent();
        }

        private int GetCurrentUserId()
        {
            return Convert.ToInt32(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        }
    }
}
