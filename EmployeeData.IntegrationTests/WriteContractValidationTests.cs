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

public sealed class WriteContractValidationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public WriteContractValidationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task Blank_project_and_task_names_are_rejected()
    {
        var administrator = await LoginAsAdministratorAsync();
        var (userId, projectId) = GetAdministratorMembership();

        using var projectRequest = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/Project?userId={userId}",
            administrator,
            new { name = "   ", description = "invalid" });
        using var projectResponse = await _client.SendAsync(
            projectRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, projectResponse.StatusCode);

        using var projectDescriptionRequest = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/Project?userId={userId}",
            administrator,
            new { name = "valid-project", description = new string('d', 1001) });
        using var projectDescriptionResponse = await _client.SendAsync(
            projectDescriptionRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, projectDescriptionResponse.StatusCode);

        using var taskRequest = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/Task?projectId={projectId}&userId={userId}",
            administrator,
            new { title = "   ", description = "invalid", isFinished = false });
        using var taskResponse = await _client.SendAsync(
            taskRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, taskResponse.StatusCode);

        using var taskDescriptionRequest = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/Task?projectId={projectId}&userId={userId}",
            administrator,
            new
            {
                title = "valid-task",
                description = new string('d', 2001),
                isFinished = false
            });
        using var taskDescriptionResponse = await _client.SendAsync(
            taskDescriptionRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, taskDescriptionResponse.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Oversized_project_and_task_fields_are_rejected()
    {
        var administrator = await LoginAsAdministratorAsync();
        var (userId, projectId) = GetAdministratorMembership();

        using var projectRequest = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/Project?userId={userId}",
            administrator,
            new { name = new string('p', 101), description = "invalid" });
        using var projectResponse = await _client.SendAsync(
            projectRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, projectResponse.StatusCode);

        using var taskRequest = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/Task?projectId={projectId}&userId={userId}",
            administrator,
            new { title = new string('t', 201), description = "invalid", isFinished = false });
        using var taskResponse = await _client.SendAsync(
            taskRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, taskResponse.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task Project_update_uses_route_id_and_persists_normalized_values()
    {
        var administrator = await LoginAsAdministratorAsync();
        var projectId = AddProject();
        var updatedName = $"updated-{Guid.NewGuid():N}";

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/api/Project/{projectId}",
            administrator,
            new
            {
                id = int.MaxValue,
                name = $"  {updatedName}  ",
                description = "  updated description  "
            });
        using var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = dbContext.Projects.AsNoTracking().Single(item => item.Id == projectId);
        Assert.Equal(updatedName, project.Name);
        Assert.Equal("updated description", project.Description);
    }

    [Fact]
    public async System.Threading.Tasks.Task Task_update_uses_route_id_and_preserves_project()
    {
        var administrator = await LoginAsAdministratorAsync();
        var (taskId, projectId) = AddTask();
        var updatedTitle = $"updated-{Guid.NewGuid():N}";

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            $"/api/Task/{taskId}",
            administrator,
            new
            {
                id = int.MaxValue,
                title = $"  {updatedTitle}  ",
                description = "  updated task  ",
                isFinished = true
            });
        using var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = dbContext.Tasks
            .AsNoTracking()
            .Where(task => task.Id == taskId)
            .Select(task => new
            {
                task.Title,
                task.Description,
                task.IsFinished,
                task.ProjectId
            })
            .Single();
        Assert.Equal(updatedTitle, state.Title);
        Assert.Equal("updated task", state.Description);
        Assert.True(state.IsFinished);
        Assert.Equal(projectId, state.ProjectId);
    }

    [Fact]
    public async System.Threading.Tasks.Task Task_title_uniqueness_is_consistent_for_employees()
    {
        var administrator = await LoginAsAdministratorAsync();
        var (_, memberProjectId) = GetAdministratorMembership();
        var employee = await CreateEmployeeAsync(administrator, memberProjectId);
        var duplicateTitle = $"duplicate-{Guid.NewGuid():N}";
        AddTask(duplicateTitle);
        var employeeToken = await LoginAsync(employee.UserName, employee.Password);

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/Task?projectId={memberProjectId}&userId={employee.Id}",
            employeeToken,
            new
            {
                title = duplicateTitle.ToUpperInvariant(),
                description = "duplicate task",
                isFinished = false
            });
        using var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(
            1,
            dbContext.Tasks.Count(task => task.Title.ToUpper() == duplicateTitle.ToUpper()));
    }

    [Fact]
    public async System.Threading.Tasks.Task Create_routes_return_created_resources_and_locations()
    {
        var administrator = await LoginAsAdministratorAsync();
        var (userId, projectId) = GetAdministratorMembership();
        var projectName = $"created-{Guid.NewGuid():N}";

        using var projectRequest = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/Project?userId={userId}",
            administrator,
            new { name = projectName, description = "created project" });
        using var projectResponse = await _client.SendAsync(
            projectRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, projectResponse.StatusCode);
        Assert.NotNull(projectResponse.Headers.Location);
        var project = await projectResponse.Content.ReadFromJsonAsync<ProjectDto>(
            TestContext.Current.CancellationToken);
        var createdProject = Assert.IsType<ProjectDto>(project);
        Assert.True(createdProject.Id > 0);
        Assert.Equal(projectName, createdProject.Name);
        Assert.Equal(
            $"/api/Project/{createdProject.Id}",
            projectResponse.Headers.Location.AbsolutePath);

        using var getProjectRequest = new HttpRequestMessage(
            HttpMethod.Get,
            projectResponse.Headers.Location)
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", administrator)
            }
        };
        using var getProjectResponse = await _client.SendAsync(
            getProjectRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getProjectResponse.StatusCode);

        var taskTitle = $"created-{Guid.NewGuid():N}";
        using var taskRequest = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/Task?projectId={projectId}&userId={userId}",
            administrator,
            new { title = taskTitle, description = "created task", isFinished = false });
        using var taskResponse = await _client.SendAsync(
            taskRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, taskResponse.StatusCode);
        Assert.NotNull(taskResponse.Headers.Location);
        var task = await taskResponse.Content.ReadFromJsonAsync<TaskDto>(
            TestContext.Current.CancellationToken);
        var createdTask = Assert.IsType<TaskDto>(task);
        Assert.True(createdTask.Id > 0);
        Assert.Equal(taskTitle, createdTask.Title);
        Assert.Equal(
            $"/api/Task/{createdTask.Id}",
            taskResponse.Headers.Location.AbsolutePath);

        using var getTaskRequest = new HttpRequestMessage(
            HttpMethod.Get,
            taskResponse.Headers.Location)
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", administrator)
            }
        };
        using var getTaskResponse = await _client.SendAsync(
            getTaskRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getTaskResponse.StatusCode);
    }

    private (int UserId, int ProjectId) GetAdministratorMembership()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.UserProjects
            .Where(userProject =>
                userProject.User.UserName == ApiWebApplicationFactory.AdministratorUserName)
            .Select(userProject => new ValueTuple<int, int>(
                userProject.UserId,
                userProject.ProjectId))
            .First();
    }

    private int AddProject()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = new Project
        {
            Name = $"original-{Guid.NewGuid():N}",
            Description = "original description"
        };
        dbContext.Projects.Add(project);
        dbContext.SaveChanges();
        return project.Id;
    }

    private (int TaskId, int ProjectId) AddTask(string? title = null)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var project = new Project
        {
            Name = $"task-project-{Guid.NewGuid():N}",
            Description = "task update project"
        };
        var task = new ProjectTask
        {
            Title = title ?? $"original-{Guid.NewGuid():N}",
            Description = "original task",
            IsFinished = false,
            Project = project
        };
        dbContext.Tasks.Add(task);
        dbContext.SaveChanges();
        return (task.Id, project.Id);
    }

    private async System.Threading.Tasks.Task<string> LoginAsAdministratorAsync()
    {
        return await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
    }

    private async System.Threading.Tasks.Task<string> LoginAsync(
        string userName,
        string password)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/User/login",
            new
            {
                username = userName,
                password
            },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(
            TestContext.Current.CancellationToken);
        return Assert.IsType<LoginResponse>(login).Token;
    }

    private async System.Threading.Tasks.Task<(int Id, string UserName, string Password)>
        CreateEmployeeAsync(string administratorToken, int projectId)
    {
        var userName = $"employee-{Guid.NewGuid():N}";
        var password = $"secure-{Guid.NewGuid():N}";
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            $"/api/User?projectId={projectId}",
            administratorToken,
            new { userName, password, role = "Employee" });
        using var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var createdUser = await response.Content.ReadFromJsonAsync<UserResponse>(
            TestContext.Current.CancellationToken);
        var user = Assert.IsType<UserResponse>(createdUser);
        return (user.Id, userName, password);
    }

    private static HttpRequestMessage CreateJsonRequest<T>(
        HttpMethod method,
        string path,
        string token,
        T body)
    {
        return new HttpRequestMessage(method, path)
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", token)
            },
            Content = JsonContent.Create(body)
        };
    }
}
