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
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ITaskService _taskService;
        private readonly IProjectService _projectService;
        private readonly IMapper _mapper;
        private readonly AppDbContext _dbContext;

        public UserController(IUserService userService, ITaskService taskService, IProjectService projectService, IMapper mapper, AppDbContext dbContext)
        {
            _userService = userService;
            _taskService = taskService;
            _projectService = projectService;
            _mapper = mapper;
            _dbContext = dbContext;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public IActionResult Login(string username, string password)
        {
            var token = _userService.Authenticate(username, password);
            if (token == null)
            {
                return Unauthorized(new { Message = "Invalid credentials" });
            }
            return Ok(new { Token = token });
        }
        
        [HttpGet]
        [Authorize(Roles = "Administrator")]
        [ProducesResponseType(200, Type = typeof(IEnumerable<User>))]
         public IActionResult GetUsers()
        {
            var users = _mapper.Map<List<UserDto>>(_userService.GetUsers());

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            return Ok(users);
        
        }
       
        [HttpGet("{id}", Name = "GetUserById")]
        [ProducesResponseType(200, Type = typeof(Project))]
        [ProducesResponseType(400)]
        public IActionResult GetUser(int id)
        {
            if (!_userService.UserExists(id))
                return NotFound();

            var user = _mapper.Map<UserDto>(_userService.GetUser(id));

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            return Ok(user);
        }
       
        [HttpGet("tasks/{taskId}/users")]
        [ProducesResponseType(200, Type = typeof(User))]
        [ProducesResponseType(400)]
        public IActionResult GetUsersOfATask(int taskId)
        {
            if (!_taskService.TaskExists(taskId))
            {
                return NotFound();
            }

            var user = _mapper.Map<List<UserDto>>(_userService.GetUsersOfATask(taskId));

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            return Ok(user);
        }
        
        [HttpGet("projects/{projectId}/users")]
        [ProducesResponseType(200, Type = typeof(User))]
        [ProducesResponseType(400)]
        public IActionResult GetUsersOfAProject(int projectId)
        {
            if (!_projectService.ProjectExists(projectId))
            {
                return NotFound();
            }

            var user = _mapper.Map<List<UserDto>>(
                _userService.GetUsersOfAProject(projectId));

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            return Ok(user);
        }

        [HttpPost]
        [Authorize(Roles = "Administrator")]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public IActionResult CreateUser([FromQuery] int? projectId, [FromQuery] int? taskId, [FromBody] UserDto userCreate)
        {
            if (userCreate == null)
                return BadRequest(ModelState);

            var users = _userService.GetUsers()
                .Where(p => p.UserName.Trim().ToUpper() == userCreate.UserName.TrimEnd().ToUpper())
                .FirstOrDefault();

            if (users != null)
            {
                ModelState.AddModelError("", "User already exists");
                return StatusCode(422, ModelState);
            }

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userMap = _mapper.Map<User>(userCreate);

            if (!_userService.CreateUser(projectId, taskId, userMap))
            {
                ModelState.AddModelError("", "Something went wrong while saving");
                return StatusCode(500, ModelState);
            }

            return Ok("Successfully created");
        }

        [HttpPut("{userId}")]
        [ProducesResponseType(400)]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public IActionResult UpdateUser(int userId,[FromBody] UserDto updatedUser)
        {
            if (updatedUser == null)
                return BadRequest(ModelState);

            if (userId != updatedUser.Id)
                return BadRequest(ModelState);

            if (!_userService.UserExists(userId))
                return NotFound();

            if (!ModelState.IsValid)
                return BadRequest();

            var userMap = _mapper.Map<User>(updatedUser);

            var existingUser = _dbContext.Users.Local.FirstOrDefault(u => u.Id == userId);

            if (existingUser != null)
            {
                userMap = existingUser;
            }
            else
            {
                _dbContext.Attach(userMap);
            }

            if (_userService.GetUserRole(_userService.GetCurrentUserId()) == "Employee")
            {
                if (userId != _userService.GetCurrentUserId())
                {
                    ModelState.AddModelError("", "Employees can only update their own profile.");
                    return StatusCode(403, ModelState);
                }
            }

            if (!_userService.UpdateUser( userMap))
            {
                ModelState.AddModelError("", "Something went wrong updating User properties.");
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
        [Authorize(Roles = "Administrator")]
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
        [Authorize(Roles = "Administrator")]
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
    }
}