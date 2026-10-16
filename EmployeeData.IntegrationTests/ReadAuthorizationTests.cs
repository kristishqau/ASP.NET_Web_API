using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EmployeeData.Dto;
using EmployeeData.Models;
using EmployeeData.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ProjectTask = EmployeeData.Models.Task;

namespace EmployeeData.IntegrationTests;

public sealed class ReadAuthorizationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ReadAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task Employees_only_see_their_projects()
    {
        var scenario = CreateScenario();
        var token = await LoginAsync(scenario.EmployeeName, scenario.EmployeePassword);

        using var listResponse = await GetAsync("/api/Project", token);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var projects = await listResponse.Content.ReadFromJsonAsync<List<ProjectDto>>(
            TestContext.Current.CancellationToken);
        Assert.Contains(projects!, project => project.Id == scenario.MemberProjectId);
        Assert.Contains(projects!, project => project.Id == scenario.EmptyProjectId);
        Assert.DoesNotContain(projects!, project => project.Id == scenario.PrivateProjectId);

        using var legacyListResponse = await GetAsync(
            "/api/Project/GetProjectsForUser",
            token);
        Assert.Equal(HttpStatusCode.OK, legacyListResponse.StatusCode);
        var legacyProjects = await legacyListResponse.Content
            .ReadFromJsonAsync<List<ProjectDto>>(TestContext.Current.CancellationToken);
        Assert.Contains(legacyProjects!, project => project.Id == scenario.MemberProjectId);
        Assert.DoesNotContain(legacyProjects!, project => project.Id == scenario.PrivateProjectId);

        using var visibleResponse = await GetAsync(
            $"/api/Project/{scenario.MemberProjectId}",
            token);
        using var hiddenResponse = await GetAsync(
            $"/api/Project/{scenario.PrivateProjectId}",
            token);
        using var missingResponse = await GetAsync($"/api/Project/{int.MaxValue}", token);
        Assert.Equal(HttpStatusCode.OK, visibleResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        using var emptyTasksResponse = await GetAsync(
            $"/api/Project/{scenario.EmptyProjectId}/tasks",
            token);
        using var hiddenTasksResponse = await GetAsync(
            $"/api/Project/{scenario.PrivateProjectId}/tasks",
            token);
        Assert.Equal(HttpStatusCode.OK, emptyTasksResponse.StatusCode);
        Assert.Empty((await emptyTasksResponse.Content.ReadFromJsonAsync<List<TaskDto>>(
            TestContext.Current.CancellationToken))!);
        Assert.Equal(HttpStatusCode.NotFound, hiddenTasksResponse.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Employees_see_team_tasks_and_direct_assignments_but_not_private_tasks()
    {
        var scenario = CreateScenario();
        var token = await LoginAsync(scenario.EmployeeName, scenario.EmployeePassword);

        using var listResponse = await GetAsync("/api/Task", token);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var tasks = await listResponse.Content.ReadFromJsonAsync<List<TaskDto>>(
            TestContext.Current.CancellationToken);
        Assert.Contains(tasks!, task => task.Id == scenario.TeamTaskId);
        Assert.Contains(tasks!, task => task.Id == scenario.DirectlyAssignedTaskId);
        Assert.DoesNotContain(tasks!, task => task.Id == scenario.PrivateTaskId);

        using var teamResponse = await GetAsync($"/api/Task/{scenario.TeamTaskId}", token);
        using var directAssignmentResponse = await GetAsync(
            $"/api/Task/{scenario.DirectlyAssignedTaskId}",
            token);
        using var hiddenResponse = await GetAsync($"/api/Task/{scenario.PrivateTaskId}", token);
        using var missingResponse = await GetAsync($"/api/Task/{int.MaxValue}", token);
        Assert.Equal(HttpStatusCode.OK, teamResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, directAssignmentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Employees_only_read_their_profile_but_can_read_visible_rosters()
    {
        var scenario = CreateScenario();
        var token = await LoginAsync(scenario.EmployeeName, scenario.EmployeePassword);

        using var selfResponse = await GetAsync($"/api/User/{scenario.EmployeeId}", token);
        using var otherResponse = await GetAsync($"/api/User/{scenario.TeammateId}", token);
        using var missingResponse = await GetAsync($"/api/User/{int.MaxValue}", token);
        Assert.Equal(HttpStatusCode.OK, selfResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        using var projectRosterResponse = await GetAsync(
            $"/api/User/projects/{scenario.MemberProjectId}/users",
            token);
        Assert.Equal(HttpStatusCode.OK, projectRosterResponse.StatusCode);
        var projectUsers = await projectRosterResponse.Content
            .ReadFromJsonAsync<List<UserResponse>>(TestContext.Current.CancellationToken);
        Assert.Contains(projectUsers!, user => user.Id == scenario.EmployeeId);
        Assert.Contains(projectUsers!, user => user.Id == scenario.TeammateId);

        using var taskRosterResponse = await GetAsync(
            $"/api/User/tasks/{scenario.TeamTaskId}/users",
            token);
        Assert.Equal(HttpStatusCode.OK, taskRosterResponse.StatusCode);
        var taskUsers = await taskRosterResponse.Content
            .ReadFromJsonAsync<List<UserResponse>>(TestContext.Current.CancellationToken);
        Assert.Contains(taskUsers!, user => user.Id == scenario.TeammateId);

        using var hiddenProjectRosterResponse = await GetAsync(
            $"/api/User/projects/{scenario.PrivateProjectId}/users",
            token);
        using var hiddenTaskRosterResponse = await GetAsync(
            $"/api/User/tasks/{scenario.PrivateTaskId}/users",
            token);
        Assert.Equal(HttpStatusCode.NotFound, hiddenProjectRosterResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenTaskRosterResponse.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Administrators_can_read_all_resources()
    {
        var scenario = CreateScenario();
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);

        var paths = new[]
        {
            $"/api/Project/{scenario.PrivateProjectId}",
            $"/api/Project/{scenario.PrivateProjectId}/tasks",
            $"/api/Task/{scenario.PrivateTaskId}",
            $"/api/User/{scenario.TeammateId}",
            $"/api/User/projects/{scenario.PrivateProjectId}/users",
            $"/api/User/tasks/{scenario.PrivateTaskId}/users"
        };

        foreach (var path in paths)
        {
            using var response = await GetAsync(path, administrator);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private ReadScenario CreateScenario()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var suffix = Guid.NewGuid().ToString("N");
        var password = $"secure-{suffix}";
        var employee = new User
        {
            UserName = $"read-employee-{suffix}",
            Role = "Employee"
        };
        employee.PasswordHash = passwordHasher.HashPassword(employee, password);
        var teammate = new User
        {
            UserName = $"read-teammate-{suffix}",
            Role = "Employee"
        };
        teammate.PasswordHash = passwordHasher.HashPassword(teammate, password);

        var teamTask = NewTask($"team-{suffix}");
        var memberProject = new Project
        {
            Name = $"member-{suffix}",
            Description = "Visible to the employee's team.",
            Tasks = new List<ProjectTask> { teamTask }
        };
        var privateTask = NewTask($"private-{suffix}");
        var directlyAssignedTask = NewTask($"direct-{suffix}");
        var privateProject = new Project
        {
            Name = $"private-{suffix}",
            Description = "Not visible to the employee.",
            Tasks = new List<ProjectTask> { privateTask, directlyAssignedTask }
        };
        var emptyProject = new Project
        {
            Name = $"empty-{suffix}",
            Description = "Visible project without tasks."
        };
        dbContext.UserProjects.AddRange(
            new UserProject { User = employee, Project = memberProject },
            new UserProject { User = teammate, Project = memberProject },
            new UserProject { User = teammate, Project = privateProject },
            new UserProject { User = employee, Project = emptyProject });
        dbContext.UserTasks.AddRange(
            new UserTask { User = teammate, Task = teamTask },
            new UserTask { User = teammate, Task = privateTask },
            new UserTask { User = employee, Task = directlyAssignedTask });
        dbContext.SaveChanges();

        return new ReadScenario(
            employee.Id,
            employee.UserName,
            password,
            teammate.Id,
            memberProject.Id,
            privateProject.Id,
            emptyProject.Id,
            teamTask.Id,
            privateTask.Id,
            directlyAssignedTask.Id);
    }

    private static ProjectTask NewTask(string title)
    {
        return new ProjectTask
        {
            Title = title,
            Description = "read authorization test task",
            IsFinished = false
        };
    }

    private async System.Threading.Tasks.Task<string> LoginAsync(
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
        return Assert.IsType<LoginResponse>(login).Token;
    }

    private async System.Threading.Tasks.Task<HttpResponseMessage> GetAsync(
        string path,
        string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record ReadScenario(
        int EmployeeId,
        string EmployeeName,
        string EmployeePassword,
        int TeammateId,
        int MemberProjectId,
        int PrivateProjectId,
        int EmptyProjectId,
        int TeamTaskId,
        int PrivateTaskId,
        int DirectlyAssignedTaskId);
}
