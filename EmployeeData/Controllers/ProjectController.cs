using AutoMapper;
using EmployeeData.Dto;
using EmployeeData.Models;
using EmployeeData.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmployeeData.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProjectController : ControllerBase
    {
        private readonly IProjectService _projectService;
        private readonly IMapper _mapper;
        private readonly AppDbContext _dbContext;

        public ProjectController(IProjectService projectService, IMapper mapper, AppDbContext dbContext)
        {
            _projectService = projectService;
            _mapper = mapper;
            _dbContext = dbContext;
        }

        [HttpGet]
        [ProducesResponseType(200, Type = typeof(IEnumerable<Project>))]
        public IActionResult GetProjects()
        {
            var projects =_mapper.Map<List<ProjectDto>>(_projectService.GetProjects());

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            return Ok(projects);
        }
        [HttpGet("{id}", Name = "GetProjectById")]
        [ProducesResponseType(200, Type = typeof(Project))]
        [ProducesResponseType(400)]
        public IActionResult GetProject(int id)
        {
            if (!_projectService.ProjectExists(id))
                return NotFound();

            var project = _mapper.Map<ProjectDto>(_projectService.GetProject(id));
            
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            return Ok(project);
        }
        [HttpGet("{projectId}/tasks", Name = "GetTasksByProject")]
        [ProducesResponseType(200, Type = typeof(IEnumerable<Task>))]
        [ProducesResponseType(400)]
        public IActionResult GetTasksByProject(int projectId)
        {
            var tasks = _mapper.Map<List<TaskDto>>(_projectService.GetTasksByProject(projectId));
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            return Ok(tasks);
        }

        [HttpPost]
        [Authorize(Roles = "Administrator")]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public IActionResult CreateProject([FromQuery] int userId, [FromBody] ProjectDto projectCreate)
        {
            if (projectCreate == null)
                return BadRequest(ModelState);

            var projects = _projectService.GetProjects()
                .Where(p => p.Name.Trim().ToUpper() == projectCreate.Name.TrimEnd().ToUpper())
                .FirstOrDefault();

            if (projects != null)
            {
                ModelState.AddModelError("", "Project already exists");
                return StatusCode(422, ModelState);
            }

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var projectMap = _mapper.Map<Project>(projectCreate);

            if (!_projectService.CreateProject(userId, projectMap))
            {
                ModelState.AddModelError("", "Something went wrong while saving");
                return StatusCode(500, ModelState);
            }

            return Ok("Successfully created");
        }

        [HttpPut("{projectId}")]
        [Authorize(Roles = "Administrator")]
        [ProducesResponseType(400)]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public IActionResult UpdateProject(int projectId, [FromBody] ProjectDto updatedProject)
        {
            if (updatedProject == null)
                return BadRequest(ModelState);

            if (projectId != updatedProject.Id)
                return BadRequest(ModelState);

            if (!_projectService.ProjectExists(projectId))
                return NotFound();

            if (!ModelState.IsValid)
                return BadRequest();

            var projectMap = _mapper.Map<Project>(updatedProject);

            var existingProject = _dbContext.Projects.Local.FirstOrDefault(p => p.Id == projectId);

            if (existingProject != null)
            {
                projectMap = existingProject;
            }
            else
            {
                _dbContext.Attach(projectMap);
            }

            if (!_projectService.UpdateProject(projectMap))
            {
                ModelState.AddModelError("", "Something went wrong updating User properties.");
                return StatusCode(500, ModelState);
            }

            return NoContent();
        }

        [HttpGet("GetProjectsForUser")]
        public IActionResult GetProjectsForUser()
        {
            try
            {
                var userId = Convert.ToInt32(User.FindFirst("id")?.Value);
                var projects = _projectService.GetProjectsForUser(userId);

                return Ok(projects);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpDelete("{projectId}")]
        [Authorize(Roles = "Administrator")]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public IActionResult DeleteProject(int projectId)
        {
            if (!_projectService.ProjectExists(projectId))
            {
                ModelState.AddModelError("", "Project not found");
                return NotFound(ModelState);
            }

            var project = _projectService.GetProject(projectId);

            if (_projectService.GetTaskIdsByProjectId(projectId).Any(taskId => !_projectService.IsTaskFinished(taskId)))
            {
                ModelState.AddModelError("", "Cannot delete project with unfinished tasks.");
                return BadRequest(ModelState);
            }

            var userProjects = _dbContext.UserProjects.Where(up => up.ProjectId == projectId).ToList();

            foreach (var userProject in userProjects)
            {
                _dbContext.UserProjects.Remove(userProject);
            }

            _dbContext.SaveChanges();

            var projectToDelete = _projectService.GetProject(projectId);

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!_projectService.DeleteProject(projectToDelete))
            {
                ModelState.AddModelError("", "Failed to delete project.");
                return StatusCode(500, ModelState);
            }

            return NoContent();
        }
    }
}
