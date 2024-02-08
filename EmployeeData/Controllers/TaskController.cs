using AutoMapper;
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
        private readonly ITaskService _taskService;
        private readonly IProjectService _projectService;
        private readonly IUserService _userService;
        private readonly IMapper _mapper;
        private readonly AppDbContext _dbContext;
        public TaskController(ITaskService taskService, IProjectService projectService, IUserService userService, IMapper mapper, AppDbContext dbContext)
        {
            _taskService = taskService;
            _projectService = projectService;
            _userService = userService;
            _mapper = mapper;
            _dbContext = dbContext;
        }

        [HttpGet]
        [ProducesResponseType(200, Type = typeof(IEnumerable<Task>))]
        public IActionResult GetTasks()
        {
            var tasks = _mapper.Map<List<TaskDto>>(_taskService.GetTasks());

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

            var task = _mapper.Map<TaskDto>(_taskService.GetTask(id));

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            return Ok(task);
        }

        [HttpPost]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public IActionResult CreateTask([FromQuery] int projectId,[FromQuery] int userId, [FromBody] TaskDto taskCreate)
        {
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

            var userRole = _taskService.GetUserRoleById(_taskService.GetCurrentUserId());
            if (userRole != "Administrator" && !_projectService.IsUserInProject(userId, projectId))
            {
                ModelState.AddModelError("", "User does not have access to the specified project");
                return StatusCode(403, ModelState);
            }

            var taskMap = _mapper.Map<Task>(taskCreate);

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
        public IActionResult UpdateTask(int userId, int taskId, [FromBody] TaskDto updatedTask)
        {
            if (updatedTask == null)
                return BadRequest(ModelState);

            if (taskId != updatedTask.Id)
                return BadRequest(ModelState);

            if (!_taskService.TaskExists(taskId))
                return NotFound();

            if (!ModelState.IsValid)
                return BadRequest();

            var taskMap = _mapper.Map<Task>(updatedTask);

            var existingTask = _dbContext.Tasks.Local.FirstOrDefault(t => t.Id == taskId);

            if (existingTask != null)
            {
                taskMap = existingTask;
            }
            else
            {
                _dbContext.Attach(taskMap);
            }

            if (_userService.GetUserRole(_userService.GetCurrentUserId()) == "Employee" && _taskService.IsUserInTask(_userService.GetCurrentUserId(), taskId))
            {
                if (userId != _userService.GetCurrentUserId())
                {
                    ModelState.AddModelError("", "Employees can only update their own tasks.");
                    return StatusCode(403, ModelState);
                }
            }

            if (!_taskService.UpdateTask(taskMap))
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
