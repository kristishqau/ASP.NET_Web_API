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

public sealed class DeleteAtomicityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DeleteAtomicityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task Project_with_unfinished_tasks_is_left_unchanged()
    {
        var token = await LoginAsAdministratorAsync();
        var graph = AddProjectGraph(isTaskFinished: false);

        using var response = await DeleteAsync($"/api/Project/{graph.ProjectId}", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            new GraphState(true, true, true, true),
            GetGraphState(graph));
    }

    [Fact]
    public async System.Threading.Tasks.Task Project_with_finished_tasks_is_deleted_as_one_unit()
    {
        var token = await LoginAsAdministratorAsync();
        var graph = AddProjectGraph(isTaskFinished: true);

        using var response = await DeleteAsync($"/api/Project/{graph.ProjectId}", token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            new GraphState(false, false, false, false),
            GetGraphState(graph));
    }

    [Fact]
    public async System.Threading.Tasks.Task Task_delete_removes_assignments_but_keeps_its_project()
    {
        var token = await LoginAsAdministratorAsync();
        var graph = AddProjectGraph(isTaskFinished: false);

        using var response = await DeleteAsync($"/api/Task/{graph.TaskId}", token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            new GraphState(true, false, true, false),
            GetGraphState(graph));
    }

    [Fact]
    public async System.Threading.Tasks.Task User_delete_removes_memberships_but_keeps_work_items()
    {
        var token = await LoginAsAdministratorAsync();
        var graph = AddUserGraph();

        using var response = await DeleteAsync($"/api/User/{graph.UserId}", token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(dbContext.Users.Any(user => user.Id == graph.UserId));
        Assert.False(dbContext.UserProjects.Any(userProject =>
            userProject.UserId == graph.UserId));
        Assert.False(dbContext.UserTasks.Any(userTask =>
            userTask.UserId == graph.UserId));
        Assert.True(dbContext.Projects.Any(project => project.Id == graph.ProjectId));
        Assert.True(dbContext.Tasks.Any(task => task.Id == graph.TaskId));
    }

    [Theory]
    [InlineData("Project")]
    [InlineData("Task")]
    [InlineData("User")]
    public async System.Threading.Tasks.Task Missing_delete_targets_return_not_found(
        string resource)
    {
        var token = await LoginAsAdministratorAsync();

        using var response = await DeleteAsync(
            $"/api/{resource}/{int.MaxValue}",
            token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private ProjectGraph AddProjectGraph(bool isTaskFinished)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var administrator = dbContext.Users.Single(user =>
            user.UserName == ApiWebApplicationFactory.AdministratorUserName);
        var suffix = Guid.NewGuid().ToString("N");
        var task = new ProjectTask
        {
            Title = $"delete-task-{suffix}",
            Description = "delete transaction test",
            IsFinished = isTaskFinished
        };
        var project = new Project
        {
            Name = $"delete-project-{suffix}",
            Description = "delete transaction test",
            Tasks = new List<ProjectTask> { task }
        };
        dbContext.UserProjects.Add(new UserProject
        {
            User = administrator,
            Project = project
        });
        dbContext.UserTasks.Add(new UserTask
        {
            User = administrator,
            Task = task
        });
        dbContext.SaveChanges();
        return new ProjectGraph(
            project.Id,
            task.Id,
            administrator.Id);
    }

    private UserGraph AddUserGraph()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User
        {
            UserName = $"delete-user-{suffix}",
            Password = string.Empty,
            PasswordHash = "not-used-by-this-test",
            Role = "Employee"
        };
        var task = new ProjectTask
        {
            Title = $"delete-user-task-{suffix}",
            Description = "delete user test",
            IsFinished = false
        };
        var project = new Project
        {
            Name = $"delete-user-project-{suffix}",
            Description = "delete user test",
            Tasks = new List<ProjectTask> { task }
        };
        dbContext.UserProjects.Add(new UserProject { User = user, Project = project });
        dbContext.UserTasks.Add(new UserTask { User = user, Task = task });
        dbContext.SaveChanges();
        return new UserGraph(user.Id, project.Id, task.Id);
    }

    private GraphState GetGraphState(ProjectGraph graph)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return new GraphState(
            dbContext.Projects.AsNoTracking().Any(project =>
                project.Id == graph.ProjectId),
            dbContext.Tasks.AsNoTracking().Any(task => task.Id == graph.TaskId),
            dbContext.UserProjects.AsNoTracking().Any(userProject =>
                userProject.ProjectId == graph.ProjectId
                && userProject.UserId == graph.UserId),
            dbContext.UserTasks.AsNoTracking().Any(userTask =>
                userTask.TaskId == graph.TaskId
                && userTask.UserId == graph.UserId));
    }

    private async System.Threading.Tasks.Task<string> LoginAsAdministratorAsync()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/User/login",
            new
            {
                username = ApiWebApplicationFactory.AdministratorUserName,
                password = ApiWebApplicationFactory.AdministratorPassword
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(
            TestContext.Current.CancellationToken);
        return Assert.IsType<LoginResponse>(login).Token;
    }

    private async System.Threading.Tasks.Task<HttpResponseMessage> DeleteAsync(
        string path,
        string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record ProjectGraph(int ProjectId, int TaskId, int UserId);

    private sealed record UserGraph(int UserId, int ProjectId, int TaskId);

    private sealed record GraphState(
        bool ProjectExists,
        bool TaskExists,
        bool ProjectMembershipExists,
        bool TaskAssignmentExists);
}
