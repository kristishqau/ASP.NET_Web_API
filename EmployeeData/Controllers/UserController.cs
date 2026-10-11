using EmployeeData.Helper;
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
    public class UserController : ControllerBase
    {
        private const string AdministratorRole = "Administrator";
        private const string EmployeeRole = "Employee";

        private readonly IUserService _userService;
        private readonly ITaskService _taskService;
        private readonly IProjectService _projectService;
        private readonly AppDbContext _dbContext;

        public UserController(IUserService userService, ITaskService taskService, IProjectService projectService, AppDbContext dbContext)
        {
            _userService = userService;
            _taskService = taskService;
            _projectService = projectService;
            _dbContext = dbContext;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [ProducesResponseType(200, Type = typeof(LoginResponse))]
        [ProducesResponseType(401)]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            var token = _userService.Authenticate(request.Username, request.Password);
            if (token == null)
            {
                return Unauthorized(new { Message = "Invalid credentials" });
            }

            var user = _dbContext.Users.FirstOrDefault(u => u.UserName == request.Username);
            if (user == null)
            {
                return Unauthorized(new { Message = "Invalid credentials" });
            }

            return Ok(new LoginResponse
            {
                Token = token,
                UserId = user.Id,
                Username = user.UserName,
                Role = user.Role,
                ProfilePictureUrl = user.ProfilePictureUrl
            });
        }

        [HttpGet]
        [Authorize(Roles = AdministratorRole)]
        [ProducesResponseType(200, Type = typeof(IEnumerable<UserResponse>))]
        public IActionResult GetUsers()
        {
            var users = _userService.GetUsers().Select(user => user.ToResponse()).ToList();
            return Ok(users);
        }

        [HttpGet("{id}", Name = "GetUserById")]
        [ProducesResponseType(200, Type = typeof(UserResponse))]
        [ProducesResponseType(404)]
        public IActionResult GetUser(int id)
        {
            if (!_userService.UserExists(id))
                return NotFound();

            var user = _userService.GetUser(id).ToResponse();

            return Ok(user);
        }

        [HttpGet("tasks/{taskId}/users")]
        [ProducesResponseType(200, Type = typeof(IEnumerable<UserResponse>))]
        [ProducesResponseType(404)]
        public IActionResult GetUsersOfATask(int taskId)
        {
            if (!_taskService.TaskExists(taskId))
            {
                return NotFound();
            }

            var user = _userService.GetUsersOfATask(taskId)
                .Select(taskUser => taskUser.ToResponse())
                .ToList();

            return Ok(user);
        }

        [HttpGet("projects/{projectId}/users")]
        [ProducesResponseType(200, Type = typeof(IEnumerable<UserResponse>))]
        [ProducesResponseType(404)]
        public IActionResult GetUsersOfAProject(int projectId)
        {
            if (!_projectService.ProjectExists(projectId))
            {
                return NotFound();
            }

            var user = _userService.GetUsersOfAProject(projectId)
                .Select(projectUser => projectUser.ToResponse())
                .ToList();

            return Ok(user);
        }

        [HttpPost]
        [Authorize(Roles = AdministratorRole)]
        [ProducesResponseType(201, Type = typeof(UserResponse))]
        [ProducesResponseType(400)]
        [ProducesResponseType(422)]
        public IActionResult CreateUser(
            [FromQuery] int? projectId,
            [FromQuery] int? taskId,
            [FromBody] CreateUserRequest request)
        {
            var role = GetCanonicalRole(request.Role);
            if (role == null)
            {
                ModelState.AddModelError(nameof(request.Role), "Role must be Administrator or Employee.");
                return ValidationProblem(ModelState);
            }

            var userName = request.UserName.Trim();
            if (userName.Length == 0)
            {
                ModelState.AddModelError(nameof(request.UserName), "User name cannot be blank.");
                return ValidationProblem(ModelState);
            }

            var users = _userService.GetUsers()
                .Where(user => string.Equals(
                    user.UserName.Trim(),
                    userName,
                    StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (users != null)
            {
                ModelState.AddModelError("", "User already exists");
                return StatusCode(422, ModelState);
            }

            request.UserName = userName;
            request.Role = role;
            var userMap = request.ToEntity();

            if (!_userService.CreateUser(projectId, taskId, userMap))
            {
                ModelState.AddModelError("", "Something went wrong while saving");
                return StatusCode(500, ModelState);
            }

            return CreatedAtRoute(
                "GetUserById",
                new { id = userMap.Id },
                userMap.ToResponse());
        }

        [HttpPut("{userId}")]
        [ProducesResponseType(400)]
        [ProducesResponseType(204)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(422)]
        public IActionResult UpdateUser(
            int userId,
            [FromBody] UpdateUserProfileRequest request)
        {
            var currentUserId = _userService.GetCurrentUserId();
            var canUpdate = User.IsInRole(AdministratorRole)
                || (User.IsInRole(EmployeeRole) && userId == currentUserId);

            if (!canUpdate)
            {
                return Forbid();
            }

            if (!_userService.UserExists(userId))
                return NotFound();

            var userName = request.UserName.Trim();
            if (userName.Length == 0)
            {
                ModelState.AddModelError(nameof(request.UserName), "User name cannot be blank.");
                return ValidationProblem(ModelState);
            }

            if (_userService.GetUsers().Any(user =>
                user.Id != userId
                && string.Equals(
                    user.UserName.Trim(),
                    userName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError(nameof(request.UserName), "User already exists.");
                return StatusCode(422, ModelState);
            }

            var user = _userService.GetUser(userId);
            if (user.UserName == userName && user.ProfilePictureUrl == request.ProfilePictureUrl)
                return NoContent();

            user.UserName = userName;
            user.ProfilePictureUrl = request.ProfilePictureUrl;

            if (!_userService.SaveUser())
            {
                ModelState.AddModelError("", "Something went wrong updating User properties.");
                return StatusCode(500, ModelState);
            }

            return NoContent();
        }

        [HttpPut("{userId}/role")]
        [Authorize(Roles = AdministratorRole)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public IActionResult SetUserRole(
            int userId,
            [FromBody] SetUserRoleRequest request)
        {
            if (!_userService.UserExists(userId))
                return NotFound();

            var role = GetCanonicalRole(request.Role);
            if (role == null)
            {
                ModelState.AddModelError(nameof(request.Role), "Role must be Administrator or Employee.");
                return ValidationProblem(ModelState);
            }

            var user = _userService.GetUser(userId);
            if (user.Role == role)
                return NoContent();

            user.Role = role;

            if (!_userService.SaveUser())
            {
                ModelState.AddModelError("", "Something went wrong updating the user role.");
                return StatusCode(500, ModelState);
            }

            return NoContent();
        }

        [HttpPost("addUserToTaskAndProject")]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public IActionResult AddUserToTaskAndProject([FromQuery] int userId, [FromQuery] int? taskId, [FromQuery] int? projectId)
        {
            if (userId <= 0)
            {
                ModelState.AddModelError("", "Invalid userId.");
                return BadRequest(ModelState);
            }

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!_userService.AddUserToTaskAndProject(userId, taskId, projectId))
            {
                ModelState.AddModelError("", "Something went wrong while saving");
                return StatusCode(500, ModelState);
            }

            return Ok("Successfully added user to task and project");
        }

        [HttpDelete("removeUserFromTaskAndProject")]
        [Authorize(Roles = AdministratorRole)]
        [ProducesResponseType(400)]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public IActionResult RemoveUserFromTaskAndProject([FromQuery] int userId, [FromQuery] int? taskId, [FromQuery] int? projectId)
        {
            if (!_userService.UserExists(userId))
            {
                ModelState.AddModelError("", "Invalid user ID.");
                return BadRequest(ModelState);
            }

            if (taskId.HasValue)
            {
                if (!_taskService.TaskExists(taskId.GetValueOrDefault()))
                {
                    ModelState.AddModelError("", "Invalid task ID.");
                    return BadRequest(ModelState);
                }
                if (!_taskService.IsUserInTask(userId, taskId.GetValueOrDefault()))
                {
                    ModelState.AddModelError("", "User is not part of the specified task.");
                    return BadRequest(ModelState);
                }
            }

            if (projectId.HasValue)
            {
                if (!_projectService.ProjectExists(projectId.GetValueOrDefault()))
                {
                    ModelState.AddModelError("", "Invalid project ID.");
                    return BadRequest(ModelState);
                }
                if (!_projectService.IsUserInProject(userId, projectId.GetValueOrDefault()))
                {
                    ModelState.AddModelError("", "User is not part of the specified project.");
                    return BadRequest(ModelState);
                }
            }

            if (!_userService.RemoveUserFromTaskAndProject(userId, taskId.GetValueOrDefault(), projectId.GetValueOrDefault()))
            {
                ModelState.AddModelError("", "Failed to remove user from task and project.");
                return StatusCode(500, ModelState);
            }

            return NoContent();
        }

        [HttpDelete("{userId}")]
        [Authorize(Roles = AdministratorRole)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public IActionResult DeleteUser(int userId)
        {
            if (!_userService.UserExists(userId))
            {
                ModelState.AddModelError("", "User not found");
                return NotFound(ModelState);
            }

            var userTasks = _dbContext.UserTasks.Where(ut => ut.UserId == userId).ToList();
            var userProjects = _dbContext.UserProjects.Where(up => up.UserId == userId).ToList();

            foreach (var userTask in userTasks)
            {
                _dbContext.UserTasks.Remove(userTask);
            }

            foreach (var userProject in userProjects)
            {
                _dbContext.UserProjects.Remove(userProject);
            }

            _dbContext.SaveChanges();

            var userToDelete = _userService.GetUser(userId);

            if (!_userService.DeleteUser(userToDelete))
            {
                ModelState.AddModelError("", "Failed to delete user");
                return StatusCode(500, ModelState);
            }

            return NoContent();
        }

        private static string GetCanonicalRole(string role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return null;

            var trimmedRole = role.Trim();

            if (string.Equals(trimmedRole, AdministratorRole, StringComparison.OrdinalIgnoreCase))
                return AdministratorRole;

            if (string.Equals(trimmedRole, EmployeeRole, StringComparison.OrdinalIgnoreCase))
                return EmployeeRole;

            return null;
        }
    }
}
