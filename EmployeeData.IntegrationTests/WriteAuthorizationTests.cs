using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EmployeeData.Dto;
using EmployeeData.Models;
using EmployeeData.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ProjectTask = EmployeeData.Models.Task;

namespace EmployeeData.IntegrationTests;

public sealed class WriteAuthorizationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public WriteAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task Employee_outside_project_cannot_create_a_task_for_a_member()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var employee = await CreateEmployeeAsync(
            administrator.Token,
            $"outside-{Guid.NewGuid():N}",
            cancellationToken);
        var employeeLogin = await LoginAsync(employee.UserName, employee.Password);
        var projectId = GetSeedProjectId();
        var title = $"forbidden-create-{Guid.NewGuid():N}";

        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            $"/api/Task?projectId={projectId}&userId={administrator.UserId}",
            employeeLogin.Token,
            new { id = 0, title, description = "not allowed", isFinished = false });
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(TaskTitleExists(title));
    }

    [Fact]
    public async System.Threading.Tasks.Task Unassigned_employee_cannot_update_a_project_task()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var projectId = GetSeedProjectId();
        var employee = await CreateEmployeeAsync(
            administrator.Token,
            $"unassigned-{Guid.NewGuid():N}",
            cancellationToken,
            projectId);
        var employeeLogin = await LoginAsync(employee.UserName, employee.Password);
        var taskId = AddTask(projectId, $"protected-{Guid.NewGuid():N}");
        var before = GetTaskState(taskId);

        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Put,
            $"/api/Task/{taskId}",
            employeeLogin.Token,
            new
            {
                id = taskId,
                title = "unauthorized change",
                description = "unauthorized change",
                isFinished = true
            });
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, GetTaskState(taskId));

        using var missingRequest = CreateAuthenticatedJsonRequest(
            HttpMethod.Put,
            $"/api/Task/{int.MaxValue}",
            employeeLogin.Token,
            new
            {
                id = int.MaxValue,
                title = "missing task",
                description = "must not reveal existence",
                isFinished = false
            });
        using var missingResponse = await _client.SendAsync(
            missingRequest,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, missingResponse.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Administrator_cannot_assign_a_new_task_to_a_nonmember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var employee = await CreateEmployeeAsync(
            administrator.Token,
            $"nonmember-{Guid.NewGuid():N}",
            cancellationToken);
        var projectId = GetSeedProjectId();
        var title = $"nonmember-task-{Guid.NewGuid():N}";

        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            $"/api/Task?projectId={projectId}&userId={employee.UserId}",
            administrator.Token,
            new { id = 0, title, description = "invalid assignee", isFinished = false });
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(TaskTitleExists(title));
    }

    [Fact]
    public async System.Threading.Tasks.Task Assigned_employee_can_update_task_without_losing_its_project()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var projectId = GetSeedProjectId();
        var employee = await CreateEmployeeAsync(
            administrator.Token,
            $"assigned-{Guid.NewGuid():N}",
            cancellationToken,
            projectId);
        var taskId = AddTask(projectId, $"assigned-task-{Guid.NewGuid():N}", employee.UserId);
        var employeeLogin = await LoginAsync(employee.UserName, employee.Password);
        var updatedTitle = $"updated-{Guid.NewGuid():N}";

        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Put,
            $"/api/Task/{taskId}",
            employeeLogin.Token,
            new
            {
                id = taskId,
                title = updatedTitle,
                description = "completed by assignee",
                isFinished = true
            });
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var state = GetTaskState(taskId);
        Assert.Equal(updatedTitle, state.Title);
        Assert.Equal("completed by assignee", state.Description);
        Assert.True(state.IsFinished);
        Assert.Equal(projectId, state.ProjectId);
    }

    [Fact]
    public async System.Threading.Tasks.Task Employee_cannot_change_project_membership()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var employee = await CreateEmployeeAsync(
            administrator.Token,
            $"membership-{Guid.NewGuid():N}",
            cancellationToken);
        var employeeLogin = await LoginAsync(employee.UserName, employee.Password);
        var projectId = GetSeedProjectId();

        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/User/addUserToTaskAndProject?userId={employee.UserId}&projectId={projectId}",
            employeeLogin.Token);
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(HasProjectMembership(employee.UserId, projectId));
    }

    [Fact]
    public async System.Threading.Tasks.Task Administrator_cannot_assign_a_task_to_an_unrelated_project()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var employee = await CreateEmployeeAsync(
            administrator.Token,
            $"mismatch-{Guid.NewGuid():N}",
            cancellationToken);
        var firstProjectId = GetSeedProjectId();
        var taskId = AddTask(firstProjectId, $"mismatch-task-{Guid.NewGuid():N}");
        var secondProjectId = AddProject($"second-{Guid.NewGuid():N}");

        using var request = CreateAuthenticatedRequest(
            HttpMethod.Post,
            $"/api/User/addUserToTaskAndProject?userId={employee.UserId}&taskId={taskId}&projectId={secondProjectId}",
            administrator.Token);
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(HasTaskAssignment(employee.UserId, taskId));
        Assert.False(HasProjectMembership(employee.UserId, secondProjectId));
    }

    [Fact]
    public async System.Threading.Tasks.Task Administrator_assignment_is_atomic_and_idempotent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var employee = await CreateEmployeeAsync(
            administrator.Token,
            $"atomic-{Guid.NewGuid():N}",
            cancellationToken);
        var projectId = GetSeedProjectId();
        var taskId = AddTask(projectId, $"atomic-task-{Guid.NewGuid():N}");
        var path = $"/api/User/addUserToTaskAndProject?userId={employee.UserId}&taskId={taskId}&projectId={projectId}";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = CreateAuthenticatedRequest(
                HttpMethod.Post,
                path,
                administrator.Token);
            using var response = await _client.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(1, CountProjectMemberships(employee.UserId, projectId));
        Assert.Equal(1, CountTaskAssignments(employee.UserId, taskId));
    }

    [Fact]
    public async System.Threading.Tasks.Task Creating_a_user_for_a_task_also_adds_project_membership()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var projectId = GetSeedProjectId();
        var taskId = AddTask(projectId, $"create-task-only-{Guid.NewGuid():N}");
        var userName = $"task-only-{Guid.NewGuid():N}";
        var password = $"secure-{Guid.NewGuid():N}";

        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            $"/api/User?taskId={taskId}",
            administrator.Token,
            new { userName, password, role = "Employee" });
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var createdUser = await response.Content.ReadFromJsonAsync<UserResponse>(
            cancellationToken);
        var userId = Assert.IsType<UserResponse>(createdUser).Id;
        Assert.True(HasTaskAssignment(userId, taskId));
        Assert.True(HasProjectMembership(userId, projectId));
    }

    [Fact]
    public async System.Threading.Tasks.Task Creating_a_user_rejects_mismatched_task_and_project_without_partial_data()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var taskProjectId = GetSeedProjectId();
        var taskId = AddTask(taskProjectId, $"create-mismatch-{Guid.NewGuid():N}");
        var unrelatedProjectId = AddProject($"create-unrelated-{Guid.NewGuid():N}");
        var userName = $"rejected-{Guid.NewGuid():N}";

        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            $"/api/User?taskId={taskId}&projectId={unrelatedProjectId}",
            administrator.Token,
            new
            {
                userName,
                password = $"secure-{Guid.NewGuid():N}",
                role = "Employee"
            });
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(UserNameExists(userName));
    }

    [Fact]
    public async System.Threading.Tasks.Task Creating_a_user_rejects_a_missing_assignment_target()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var userName = $"missing-target-{Guid.NewGuid():N}";

        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            $"/api/User?projectId={int.MaxValue}",
            administrator.Token,
            new
            {
                userName,
                password = $"secure-{Guid.NewGuid():N}",
                role = "Employee"
            });
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(UserNameExists(userName));
    }

    [Fact]
    public async System.Threading.Tasks.Task Removing_assignments_requires_a_target_and_matching_project()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var projectId = GetSeedProjectId();
        var employee = await CreateEmployeeAsync(
            administrator.Token,
            $"remove-validate-{Guid.NewGuid():N}",
            cancellationToken,
            projectId);
        var taskId = AddTask(
            projectId,
            $"remove-validate-task-{Guid.NewGuid():N}",
            employee.UserId);
        var unrelatedProjectId = AddProject($"remove-unrelated-{Guid.NewGuid():N}");

        using var emptyRequest = CreateAuthenticatedRequest(
            HttpMethod.Delete,
            $"/api/User/removeUserFromTaskAndProject?userId={employee.UserId}",
            administrator.Token);
        using var emptyResponse = await _client.SendAsync(emptyRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);

        using var mismatchRequest = CreateAuthenticatedRequest(
            HttpMethod.Delete,
            $"/api/User/removeUserFromTaskAndProject?userId={employee.UserId}&taskId={taskId}&projectId={unrelatedProjectId}",
            administrator.Token);
        using var mismatchResponse = await _client.SendAsync(
            mismatchRequest,
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, mismatchResponse.StatusCode);
        Assert.True(HasTaskAssignment(employee.UserId, taskId));
        Assert.True(HasProjectMembership(employee.UserId, projectId));
    }

    [Fact]
    public async System.Threading.Tasks.Task Project_membership_cannot_be_removed_while_task_assignments_remain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var projectId = GetSeedProjectId();
        var employee = await CreateEmployeeAsync(
            administrator.Token,
            $"remove-safe-{Guid.NewGuid():N}",
            cancellationToken,
            projectId);
        var firstTaskId = AddTask(
            projectId,
            $"remove-first-{Guid.NewGuid():N}",
            employee.UserId);
        var secondTaskId = AddTask(
            projectId,
            $"remove-second-{Guid.NewGuid():N}",
            employee.UserId);

        using var request = CreateAuthenticatedRequest(
            HttpMethod.Delete,
            $"/api/User/removeUserFromTaskAndProject?userId={employee.UserId}&taskId={firstTaskId}&projectId={projectId}",
            administrator.Token);
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(HasTaskAssignment(employee.UserId, firstTaskId));
        Assert.True(HasTaskAssignment(employee.UserId, secondTaskId));
        Assert.True(HasProjectMembership(employee.UserId, projectId));
    }

    [Fact]
    public async System.Threading.Tasks.Task Task_and_project_membership_are_removed_together_when_safe()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var projectId = GetSeedProjectId();
        var employee = await CreateEmployeeAsync(
            administrator.Token,
            $"remove-pair-{Guid.NewGuid():N}",
            cancellationToken,
            projectId);
        var taskId = AddTask(
            projectId,
            $"remove-pair-task-{Guid.NewGuid():N}",
            employee.UserId);

        using var request = CreateAuthenticatedRequest(
            HttpMethod.Delete,
            $"/api/User/removeUserFromTaskAndProject?userId={employee.UserId}&taskId={taskId}&projectId={projectId}",
            administrator.Token);
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(HasTaskAssignment(employee.UserId, taskId));
        Assert.False(HasProjectMembership(employee.UserId, projectId));
    }

    private async System.Threading.Tasks.Task<LoginResponse> LoginAsync(
        string userName,
        string password)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/User/login",
            new { username = userName, password },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(
            TestContext.Current.CancellationToken);
        return Assert.IsType<LoginResponse>(login);
    }

    private async System.Threading.Tasks.Task<TestEmployee> CreateEmployeeAsync(
        string administratorToken,
        string userName,
        CancellationToken cancellationToken,
        int? projectId = null)
    {
        var password = $"secure-{Guid.NewGuid():N}";
        var path = projectId.HasValue
            ? $"/api/User?projectId={projectId.Value}"
            : "/api/User";
        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            path,
            administratorToken,
            new
            {
                userName,
                password,
                role = "Employee"
            });
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var createdUser = await response.Content.ReadFromJsonAsync<UserResponse>(
            cancellationToken);
        return new TestEmployee(
            Assert.IsType<UserResponse>(createdUser).Id,
            userName,
            password);
    }

    private int GetSeedProjectId()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.Projects
            .Where(project => project.Tasks.Any())
            .Select(project => project.Id)
            .First();
    }

    private int AddProject(string name)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = new Project { Name = name, Description = "test project" };
        dbContext.Projects.Add(project);
        dbContext.SaveChanges();
        return project.Id;
    }

    private int AddTask(int projectId, string title, int? assignedUserId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = dbContext.Projects.Single(candidate => candidate.Id == projectId);
        var task = new ProjectTask
        {
            Title = title,
            Description = "test task",
            IsFinished = false,
            Project = project
        };
        dbContext.Tasks.Add(task);

        if (assignedUserId.HasValue)
        {
            dbContext.UserTasks.Add(new UserTask
            {
                UserId = assignedUserId.Value,
                Task = task
            });
        }

        dbContext.SaveChanges();
        return task.Id;
    }

    private TaskState GetTaskState(int taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.Tasks
            .AsNoTracking()
            .Where(task => task.Id == taskId)
            .Select(task => new TaskState(
                task.Title,
                task.Description,
                task.IsFinished,
                task.ProjectId))
            .Single();
    }

    private bool TaskTitleExists(string title)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.Tasks.Any(task => task.Title == title);
    }

    private bool UserNameExists(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.Users.Any(user => user.UserName == userName);
    }

    private bool HasProjectMembership(int userId, int projectId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.UserProjects.Any(userProject =>
            userProject.UserId == userId
            && userProject.ProjectId == projectId);
    }

    private bool HasTaskAssignment(int userId, int taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.UserTasks.Any(userTask =>
            userTask.UserId == userId
            && userTask.TaskId == taskId);
    }

    private int CountProjectMemberships(int userId, int projectId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.UserProjects.Count(userProject =>
            userProject.UserId == userId
            && userProject.ProjectId == projectId);
    }

    private int CountTaskAssignments(int userId, int taskId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.UserTasks.Count(userTask =>
            userTask.UserId == userId
            && userTask.TaskId == taskId);
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string path,
        string token)
    {
        return new HttpRequestMessage(method, path)
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", token)
            }
        };
    }

    private static HttpRequestMessage CreateAuthenticatedJsonRequest<T>(
        HttpMethod method,
        string path,
        string token,
        T body)
    {
        var request = CreateAuthenticatedRequest(method, path, token);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private sealed record TestEmployee(int UserId, string UserName, string Password);

    private sealed record TaskState(
        string Title,
        string? Description,
        bool IsFinished,
        int ProjectId);
}
