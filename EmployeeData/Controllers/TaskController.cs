using EmployeeData.Helper;
using EmployeeData.Dto;
using EmployeeData.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        public TaskController(ITaskService taskService, IProjectService projectService, IUserService userService)
        {
            _taskService = taskService;
            _projectService = projectService;
            _userService = userService;
        }

        [HttpGet]
        [ProducesResponseType(200, Type = typeof(IEnumerable<TaskDto>))]
        public IActionResult GetTasks([FromQuery] PaginationQuery pagination)
        {
            var pageRequest = pagination.ToPageRequest();
            var page = _taskService.GetTasksForUser(
                _taskService.GetCurrentUserId(),
                User.IsInRole(AdministratorRole),
                pageRequest);
            PaginationHeaders.Add(Response, pageRequest, page.TotalCount);
            var tasks = page.Items
                .Select(task => task.ToDto())
                .ToList();
            return Ok(tasks);
        }
        [HttpGet("{id}", Name = "GetTaskById")]
        [ProducesResponseType(200, Type = typeof(TaskDto))]
        [ProducesResponseType(404)]
        public IActionResult GetTask(int id)
        {
            var task = _taskService.GetTaskForUser(
                _taskService.GetCurrentUserId(),
                User.IsInRole(AdministratorRole),
                id);
            if (task == null)
                return NotFound();

            return Ok(task.ToDto());
        }

        [HttpPost]
        [ProducesResponseType(201, Type = typeof(TaskDto))]
        [ProducesResponseType(400)]
        [ProducesResponseType(422)]
        public IActionResult CreateTask(
            [FromQuery] int projectId,
            [FromQuery] int userId,
            [FromBody] TaskRequest taskCreate)
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
                return ValidationProblem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    modelStateDictionary: ModelState);
            }

            taskCreate.Title = taskCreate.Title.Trim();
            taskCreate.Description = taskCreate.Description?.Trim() ?? string.Empty;
            if (taskCreate.Title.Length == 0)
            {
                ModelState.AddModelError(nameof(taskCreate.Title), "Task title cannot be blank.");
                return ValidationProblem(ModelState);
            }

            if (_taskService.TaskTitleExists(taskCreate.Title))
            {
                ModelState.AddModelError("", "Task already exists");
                return ValidationProblem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    modelStateDictionary: ModelState);
            }

            if (!_projectService.HasProjectMembership(userId, projectId))
            {
                ModelState.AddModelError(
                    nameof(userId),
                    "The assignee must be a member of the specified project.");
                return ValidationProblem(ModelState);
            }

            var taskMap = taskCreate.ToEntity();

            var project = _projectService.GetProject(projectId);
            if (project == null)
                return NotFound();

            taskMap.Project = project;


            if (!_taskService.CreateTask(userId, taskMap))
            {
                ModelState.AddModelError("", "Something went wrong while saving");
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "The request could not be completed.");
            }

            return CreatedAtRoute(
                "GetTaskById",
                new { id = taskMap.Id },
                taskMap.ToDto());
        }

        [HttpPut("{taskId}")]
        [ProducesResponseType(400)]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        [ProducesResponseType(422)]
        public IActionResult UpdateTask(
            int taskId,
            [FromBody] TaskRequest updatedTask)
        {
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

            updatedTask.Title = updatedTask.Title.Trim();
            updatedTask.Description = updatedTask.Description?.Trim() ?? string.Empty;
            if (updatedTask.Title.Length == 0)
            {
                ModelState.AddModelError(nameof(updatedTask.Title), "Task title cannot be blank.");
                return ValidationProblem(ModelState);
            }

            if (_taskService.TaskTitleExists(updatedTask.Title, taskId))
            {
                ModelState.AddModelError(nameof(updatedTask.Title), "Task already exists.");
                return ValidationProblem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    modelStateDictionary: ModelState);
            }

            var task = _taskService.GetTask(taskId);
            if (task == null)
                return NotFound();

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
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "The request could not be completed.");
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
            var result = _taskService.DeleteTask(taskId);
            if (result == DeletionResult.NotFound)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Task not found");
            }

            if (result != DeletionResult.Succeeded)
            {
                ModelState.AddModelError("", "Failed to delete task");
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "The request could not be completed.");
            }

            return NoContent();
        }
    } 
}
