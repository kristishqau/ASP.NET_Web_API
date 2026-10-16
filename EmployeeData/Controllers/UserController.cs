using EmployeeData.Helper;
using EmployeeData.Dto;
using EmployeeData.Security;
using EmployeeData.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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

        private readonly IAuthenticationService _authenticationService;
        private readonly IUserService _userService;

        public UserController(
            IAuthenticationService authenticationService,
            IUserService userService)
        {
            _authenticationService = authenticationService;
            _userService = userService;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting(LoginRateLimitPolicy.Name)]
        [ProducesResponseType(200, Type = typeof(LoginResponse))]
        [ProducesResponseType(401)]
        [ProducesResponseType(429)]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            var result = _authenticationService.Authenticate(
                request.Username,
                request.Password);
            if (result == null)
            {
                return Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Invalid credentials");
            }

            return Ok(new LoginResponse
            {
                Token = result.Token,
                UserId = result.UserId,
                Username = result.UserName,
                Role = result.Role,
                ProfilePictureUrl = result.ProfilePictureUrl
            });
        }

        [HttpGet]
        [Authorize(Roles = AdministratorRole)]
        [ProducesResponseType(200, Type = typeof(IEnumerable<UserResponse>))]
        public IActionResult GetUsers([FromQuery] PaginationQuery pagination)
        {
            var pageRequest = pagination.ToPageRequest();
            var page = _userService.GetUsers(pageRequest);
            PaginationHeaders.Add(Response, pageRequest, page.TotalCount);
            var users = page.Items.Select(user => user.ToResponse()).ToList();
            return Ok(users);
        }

        [HttpGet("{id}", Name = "GetUserById")]
        [ProducesResponseType(200, Type = typeof(UserResponse))]
        [ProducesResponseType(404)]
        public IActionResult GetUser(int id)
        {
            var user = _userService.GetUserForUser(
                _userService.GetCurrentUserId(),
                User.IsInRole(AdministratorRole),
                id);
            if (user == null)
                return NotFound();

            return Ok(user.ToResponse());
        }

        [HttpGet("tasks/{taskId}/users")]
        [ProducesResponseType(200, Type = typeof(IEnumerable<UserResponse>))]
        [ProducesResponseType(404)]
        public IActionResult GetUsersOfATask(
            int taskId,
            [FromQuery] PaginationQuery pagination)
        {
            var pageRequest = pagination.ToPageRequest();
            var page = _userService.GetUsersOfATaskForUser(
                _userService.GetCurrentUserId(),
                User.IsInRole(AdministratorRole),
                taskId,
                pageRequest);
            if (page == null)
                return NotFound();

            PaginationHeaders.Add(Response, pageRequest, page.TotalCount);
            var response = page.Items
                .Select(taskUser => taskUser.ToResponse())
                .ToList();

            return Ok(response);
        }

        [HttpGet("projects/{projectId}/users")]
        [ProducesResponseType(200, Type = typeof(IEnumerable<UserResponse>))]
        [ProducesResponseType(404)]
        public IActionResult GetUsersOfAProject(
            int projectId,
            [FromQuery] PaginationQuery pagination)
        {
            var pageRequest = pagination.ToPageRequest();
            var page = _userService.GetUsersOfAProjectForUser(
                _userService.GetCurrentUserId(),
                User.IsInRole(AdministratorRole),
                projectId,
                pageRequest);
            if (page == null)
                return NotFound();

            PaginationHeaders.Add(Response, pageRequest, page.TotalCount);
            var response = page.Items
                .Select(projectUser => projectUser.ToResponse())
                .ToList();

            return Ok(response);
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

            if (_userService.UserNameExists(userName))
            {
                ModelState.AddModelError("", "User already exists");
                return ValidationProblem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    modelStateDictionary: ModelState);
            }

            request.UserName = userName;
            request.Role = role;
            var userMap = request.ToEntity();

            var assignmentResult = _userService.CreateUser(
                projectId,
                taskId,
                userMap,
                request.Password);
            if (assignmentResult != UserAssignmentResult.Succeeded)
                return AssignmentFailure(assignmentResult, "Something went wrong while saving the user.");

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

            if (_userService.UserNameExists(userName, userId))
            {
                ModelState.AddModelError(nameof(request.UserName), "User already exists.");
                return ValidationProblem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    modelStateDictionary: ModelState);
            }

            var user = _userService.GetUser(userId);
            if (user == null)
                return NotFound();

            if (user.UserName == userName && user.ProfilePictureUrl == request.ProfilePictureUrl)
                return NoContent();

            user.UserName = userName;
            user.ProfilePictureUrl = request.ProfilePictureUrl;

            if (!_userService.SaveUser())
            {
                ModelState.AddModelError("", "Something went wrong updating User properties.");
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "The request could not be completed.");
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
            var role = GetCanonicalRole(request.Role);
            if (role == null)
            {
                ModelState.AddModelError(nameof(request.Role), "Role must be Administrator or Employee.");
                return ValidationProblem(ModelState);
            }

            var result = _userService.SetUserRole(userId, role);
            if (result == UserChangeResult.NotFound)
                return NotFound();

            if (result == UserChangeResult.LastAdministrator)
            {
                ModelState.AddModelError(
                    nameof(request.Role),
                    "The last administrator cannot be demoted.");
                return ValidationProblem(ModelState);
            }

            if (result == UserChangeResult.SaveFailed)
            {
                ModelState.AddModelError("", "Something went wrong updating the user role.");
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "The request could not be completed.");
            }

            return NoContent();
        }

        [HttpPost("addUserToTaskAndProject")]
        [Authorize(Roles = AdministratorRole)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        public IActionResult AddUserToTaskAndProject([FromQuery] int userId, [FromQuery] int? taskId, [FromQuery] int? projectId)
        {
            var assignmentResult = _userService.AddUserToTaskAndProject(
                userId,
                taskId,
                projectId);
            if (assignmentResult != UserAssignmentResult.Succeeded)
                return AssignmentFailure(assignmentResult, "Something went wrong while saving the assignment.");

            return NoContent();
        }

        [HttpDelete("removeUserFromTaskAndProject")]
        [Authorize(Roles = AdministratorRole)]
        [ProducesResponseType(400)]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public IActionResult RemoveUserFromTaskAndProject([FromQuery] int userId, [FromQuery] int? taskId, [FromQuery] int? projectId)
        {
            var assignmentResult = _userService.RemoveUserFromTaskAndProject(
                userId,
                taskId,
                projectId);
            if (assignmentResult != UserAssignmentResult.Succeeded)
                return AssignmentFailure(assignmentResult, "Failed to remove the assignment.");

            return NoContent();
        }

        [HttpDelete("{userId}")]
        [Authorize(Roles = AdministratorRole)]
        [ProducesResponseType(204)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public IActionResult DeleteUser(int userId)
        {
            var result = _userService.DeleteUser(userId);
            if (result == UserChangeResult.NotFound)
            {
                return Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "User not found");
            }

            if (result == UserChangeResult.LastAdministrator)
            {
                ModelState.AddModelError("", "The last administrator cannot be deleted.");
                return ValidationProblem(ModelState);
            }

            if (result != UserChangeResult.Succeeded)
            {
                ModelState.AddModelError("", "Failed to delete user");
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "The request could not be completed.");
            }

            return NoContent();
        }

        private IActionResult AssignmentFailure(
            UserAssignmentResult result,
            string saveFailureMessage)
        {
            var message = result switch
            {
                UserAssignmentResult.InvalidSelection => "A taskId or projectId is required.",
                UserAssignmentResult.UserNotFound => "The specified user does not exist.",
                UserAssignmentResult.TaskNotFound => "The specified task does not exist.",
                UserAssignmentResult.ProjectNotFound => "The specified project does not exist.",
                UserAssignmentResult.TaskProjectMismatch => "The task does not belong to the specified project.",
                UserAssignmentResult.UserNotAssignedToTask => "The user is not assigned to the specified task.",
                UserAssignmentResult.UserNotMemberOfProject => "The user is not a member of the specified project.",
                UserAssignmentResult.ProjectHasTaskAssignments => "Remove the user's other task assignments before removing project membership.",
                UserAssignmentResult.SaveFailed => saveFailureMessage,
                _ => "The assignment request is invalid."
            };

            ModelState.AddModelError("", message);
            if (result == UserAssignmentResult.SaveFailed)
            {
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "The request could not be completed.");
            }

            return ValidationProblem(ModelState);
        }

        private static string? GetCanonicalRole(string? role)
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
