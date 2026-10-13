using EmployeeData.Helper;
using EmployeeData.Dto;
using EmployeeData.Models;
using EmployeeData.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;

namespace EmployeeData.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TaskController : ControllerBase
    {
        private const string AdministratorRole = "Administrator";

        private readonly ITaskService _taskService;
        private readonly IProjectService _projectService;
        private readonly IUserService _userService;
        private readonly AppDbContext _dbContext;
        public TaskController(ITaskService taskService, IProjectService projectService, IUserService userService, AppDbContext dbContext)
        {
            _taskService = taskService;
            _projectService = projectService;
            _userService = userService;
            _dbContext = dbContext;
        }

        [HttpGet]
        [ProducesResponseType(200, Type = typeof(IEnumerable<Task>))]
        public IActionResult GetTasks()
        {
            var tasks = _taskService.GetTasks().Select(task => task.ToDto()).ToList();

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            return Ok(tasks);
        }
        [HttpGet("{id}", Name = "GetTaskById")]
        [ProducesResponseType(200, Type = typeof(Task))]
        [ProducesResponseType(400)]
        public IActionResult GetTask(int id)
        {
            if (!_taskService.TaskExists(id))
                return NotFound();

            var task = _taskService.GetTask(id).ToDto();

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            return Ok(task);
        }

        [HttpPost]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public IActionResult CreateTask([FromQuery] int projectId,[FromQuery] int userId, [FromBody] TaskDto taskCreate)
        {
            var currentUserId = _taskService.GetCurrentUserId();
            var userRole = _taskService.GetUserRoleById(currentUserId);
            var canCreate = userRole == AdministratorRole
                || (userRole == "Employee"
                    && _projectService.HasProjectMembership(currentUserId, projectId));
            if (!canCreate)
            {
                return Forbid();
            }

            if (!(_userService.UserExists(userId) && _projectService.ProjectExists(projectId)))
            {
                ModelState.AddModelError("", "Enter the corrects Id inputs for both user and project.");
                return StatusCode(422, ModelState);
            }

            if (taskCreate == null)
                return BadRequest(ModelState);

            var existingTask = _taskService.GetTasks()
                .Where(p => p.Title.Trim().ToUpper() == taskCreate.Title.TrimEnd().ToUpper())
                .FirstOrDefault();

            if (existingTask != null)
            {
                ModelState.AddModelError("", "Task already exists");
                return StatusCode(422, ModelState);
            }

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!_projectService.HasProjectMembership(userId, projectId))
            {
                ModelState.AddModelError(
                    nameof(userId),
                    "The assignee must be a member of the specified project.");
                return ValidationProblem(ModelState);
            }

            var taskMap = taskCreate.ToEntity();

            taskMap.Project = _projectService.GetProject(projectId);


            if (!_taskService.CreateTask(userId, taskMap))
            {
                ModelState.AddModelError("", "Something went wrong while saving");
                return StatusCode(500, ModelState);
            }

            return Ok("Successfully created");
        }

        [HttpPut("{taskId}")]
        [ProducesResponseType(400)]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public IActionResult UpdateTask(int taskId, [FromBody] TaskDto updatedTask)
        {
            if (updatedTask == null)
                return BadRequest(ModelState);

            if (taskId != updatedTask.Id)
                return BadRequest(ModelState);

            var currentUserId = _userService.GetCurrentUserId();
            var userRole = _userService.GetUserRole(currentUserId);
            if (userRole == AdministratorRole)
            {
                if (!_taskService.TaskExists(taskId))
                    return NotFound();
            }
            else if (userRole == "Employee")
            {
                if (!_taskService.HasTaskAssignment(currentUserId, taskId))
                    return Forbid();
            }
            else
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
                return BadRequest();

            var task = _taskService.GetTask(taskId);
            if (task.Title == updatedTask.Title
                && task.Description == updatedTask.Description
                && task.IsFinished == updatedTask.IsFinished)
            {
                return NoContent();
            }

            task.Title = updatedTask.Title;
            task.Description = updatedTask.Description;
            task.IsFinished = updatedTask.IsFinished;

            if (!_taskService.SaveTask())
            {
                ModelState.AddModelError("", "Something went wrong updating User properties.");
                return StatusCode(500, ModelState);
            }

            return NoContent();
        }

        [HttpDelete("{taskId}")]
        [Authorize(Roles = "Administrator")]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public IActionResult DeleteTask(int taskId)
        {
            if (!_taskService.TaskExists(taskId))
            {
                ModelState.AddModelError("", "Task not found");
                return NotFound(ModelState);
            }

            var userTasks = _dbContext.UserTasks.Where(ut => ut.TaskId == taskId).ToList();

            foreach (var userTask in userTasks)
            {
                _dbContext.UserTasks.Remove(userTask);
            }

            _dbContext.SaveChanges();

            var taskToDelete = _taskService.GetTask(taskId);

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!_taskService.DeleteTask(taskToDelete))
            {
                ModelState.AddModelError("", "Failed to delete task");
                return StatusCode(500, ModelState);
            }

            return NoContent();
        }
    } 
}
