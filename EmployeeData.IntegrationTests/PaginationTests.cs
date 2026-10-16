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

public sealed class PaginationTests
{
    [Fact]
    public async System.Threading.Tasks.Task Collection_routes_return_ordered_array_pages_and_headers()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = CreateClient(factory);
        var token = await LoginAsAdministrator(client);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var index = 0; index < 4; index++)
            {
                dbContext.Projects.Add(new Project
                {
                    Name = $"pagination-project-{index}-{Guid.NewGuid():N}"
                });
            }

            dbContext.SaveChanges();
        }

        var expectedProjectIds = GetProjectIds(factory).Skip(2).Take(2).ToArray();
        using var projectsRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/Project?pageNumber=2&pageSize=2",
            token,
            includeOrigin: true);
        using var projectsResponse = await client.SendAsync(
            projectsRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, projectsResponse.StatusCode);
        var projects = await projectsResponse.Content.ReadFromJsonAsync<List<ProjectDto>>(
            TestContext.Current.CancellationToken);
        Assert.Equal(expectedProjectIds, Assert.IsType<List<ProjectDto>>(projects).Select(
            project => project.Id));
        AssertPaginationHeaders(projectsResponse, pageNumber: 2, pageSize: 2, totalCount: 5);
        Assert.Contains(
            "X-Total-Count",
            projectsResponse.Headers.GetValues("Access-Control-Expose-Headers").Single(),
            StringComparison.OrdinalIgnoreCase);

        using var tasksRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/Task?pageNumber=2&pageSize=2",
            token);
        using var tasksResponse = await client.SendAsync(
            tasksRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, tasksResponse.StatusCode);
        var tasks = await tasksResponse.Content.ReadFromJsonAsync<List<TaskDto>>(
            TestContext.Current.CancellationToken);
        Assert.Single(Assert.IsType<List<TaskDto>>(tasks));
        AssertPaginationHeaders(tasksResponse, pageNumber: 2, pageSize: 2, totalCount: 3);

        using var usersRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/User?pageNumber=1&pageSize=1",
            token);
        using var usersResponse = await client.SendAsync(
            usersRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, usersResponse.StatusCode);
        var users = await usersResponse.Content.ReadFromJsonAsync<List<UserResponse>>(
            TestContext.Current.CancellationToken);
        Assert.Single(Assert.IsType<List<UserResponse>>(users));
        AssertPaginationHeaders(usersResponse, pageNumber: 1, pageSize: 1, totalCount: 1);

        var (projectId, assignedTaskId) = GetSeedRelationshipIds(factory);
        await AssertRelatedPage(
            client,
            token,
            $"/api/Project/{projectId}/tasks?pageNumber=2&pageSize=2",
            expectedItems: 1,
            totalCount: 3,
            pageNumber: 2);
        await AssertRelatedPage(
            client,
            token,
            $"/api/User/projects/{projectId}/users?pageNumber=1&pageSize=1",
            expectedItems: 1,
            totalCount: 1,
            pageSize: 1);
        await AssertRelatedPage(
            client,
            token,
            $"/api/User/tasks/{assignedTaskId}/users?pageNumber=1&pageSize=1",
            expectedItems: 1,
            totalCount: 1,
            pageSize: 1);
        await AssertRelatedPage(
            client,
            token,
            "/api/Project/GetProjectsForUser?pageNumber=3&pageSize=2",
            expectedItems: 1,
            totalCount: 5,
            pageNumber: 3);
    }

    [Fact]
    public async System.Threading.Tasks.Task Pagination_totals_are_calculated_after_authorization_filters()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = CreateClient(factory);
        var administratorToken = await LoginAsAdministrator(client);
        var projectId = GetSeedRelationshipIds(factory).ProjectId;
        var userName = $"pagination-employee-{Guid.NewGuid():N}";
        const string password = "pagination-password-12345";
        using var createUserRequest = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            $"/api/User?projectId={projectId}",
            administratorToken,
            new { userName, password, role = "Employee" });
        using var createUserResponse = await client.SendAsync(
            createUserRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createUserResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Tasks.Add(new ProjectTask
            {
                Title = $"hidden-task-{Guid.NewGuid():N}",
                Project = new Project { Name = $"hidden-project-{Guid.NewGuid():N}" }
            });
            dbContext.SaveChanges();
        }

        var employeeToken = await Login(client, userName, password);
        using var projectsRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/Project?pageSize=100",
            employeeToken);
        using var projectsResponse = await client.SendAsync(
            projectsRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, projectsResponse.StatusCode);
        AssertPaginationHeaders(projectsResponse, pageNumber: 1, pageSize: 100, totalCount: 1);

        using var tasksRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/Task?pageSize=100",
            employeeToken);
        using var tasksResponse = await client.SendAsync(
            tasksRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, tasksResponse.StatusCode);
        AssertPaginationHeaders(tasksResponse, pageNumber: 1, pageSize: 100, totalCount: 3);
    }

    [Theory]
    [InlineData("/api/Project?pageNumber=0")]
    [InlineData("/api/Project?pageSize=0")]
    [InlineData("/api/Project?pageSize=101")]
    public async System.Threading.Tasks.Task Invalid_pagination_is_rejected(string path)
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = CreateClient(factory);
        var token = await LoginAsAdministrator(client);
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, path, token);
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    private static async System.Threading.Tasks.Task<string> LoginAsAdministrator(
        HttpClient client)
    {
        return await Login(
            client,
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
    }

    private static async System.Threading.Tasks.Task<string> Login(
        HttpClient client,
        string userName,
        string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/User/login",
            new { username = userName, password },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(
            TestContext.Current.CancellationToken);
        return Assert.IsType<LoginResponse>(login).Token;
    }

    private static int[] GetProjectIds(ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.Projects
            .AsNoTracking()
            .OrderBy(project => project.Id)
            .Select(project => project.Id)
            .ToArray();
    }

    private static (int ProjectId, int AssignedTaskId) GetSeedRelationshipIds(
        ApiWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var projectId = dbContext.UserProjects
            .Where(userProject => userProject.User.UserName ==
                ApiWebApplicationFactory.AdministratorUserName)
            .Select(userProject => userProject.ProjectId)
            .Single();
        var taskId = dbContext.UserTasks
            .Where(userTask => userTask.User.UserName ==
                ApiWebApplicationFactory.AdministratorUserName)
            .Select(userTask => userTask.TaskId)
            .Single();
        return (projectId, taskId);
    }

    private static async System.Threading.Tasks.Task AssertRelatedPage(
        HttpClient client,
        string token,
        string path,
        int expectedItems,
        int totalCount,
        int pageNumber = 1,
        int pageSize = 2)
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, path, token);
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<object>>(
            TestContext.Current.CancellationToken);
        Assert.Equal(expectedItems, Assert.IsType<List<object>>(body).Count);
        AssertPaginationHeaders(response, pageNumber, pageSize, totalCount);
    }

    private static void AssertPaginationHeaders(
        HttpResponseMessage response,
        int pageNumber,
        int pageSize,
        int totalCount)
    {
        Assert.Equal(pageNumber.ToString(), GetHeader(response, "X-Page-Number"));
        Assert.Equal(pageSize.ToString(), GetHeader(response, "X-Page-Size"));
        Assert.Equal(totalCount.ToString(), GetHeader(response, "X-Total-Count"));
    }

    private static string GetHeader(HttpResponseMessage response, string name)
    {
        return Assert.Single(response.Headers.GetValues(name));
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string path,
        string token,
        bool includeOrigin = false)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (includeOrigin)
            request.Headers.Add("Origin", "https://client.example");
        return request;
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
}
