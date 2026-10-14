using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EmployeeData.Dto;
using EmployeeData.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EmployeeData.IntegrationTests;

public sealed class AdministratorContinuityTests
{
    [Fact]
    public async Task Last_administrator_cannot_be_demoted_or_deleted()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = CreateClient(factory);
        var administrator = await LoginAsync(client);

        using var demoteRequest = CreateJsonRequest(
            HttpMethod.Put,
            $"/api/User/{administrator.UserId}/role",
            administrator.Token,
            new { role = "Employee" });
        using var demoteResponse = await client.SendAsync(
            demoteRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, demoteResponse.StatusCode);

        using var deleteRequest = CreateRequest(
            HttpMethod.Delete,
            $"/api/User/{administrator.UserId}",
            administrator.Token);
        using var deleteResponse = await client.SendAsync(
            deleteRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, deleteResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storedAdministrator = dbContext.Users.Single(user =>
            user.Id == administrator.UserId);
        Assert.Equal("Administrator", storedAdministrator.Role);
    }

    [Fact]
    public async Task Administrator_can_be_demoted_or_deleted_when_another_remains()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = CreateClient(factory);
        var administrator = await LoginAsync(client);
        var secondAdministrator = await CreateAdministratorAsync(
            client,
            administrator.Token);

        using var demoteRequest = CreateJsonRequest(
            HttpMethod.Put,
            $"/api/User/{secondAdministrator.Id}/role",
            administrator.Token,
            new { role = "Employee" });
        using var demoteResponse = await client.SendAsync(
            demoteRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, demoteResponse.StatusCode);

        using (var roleScope = factory.Services.CreateScope())
        {
            var roleDbContext = roleScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(
                "Employee",
                roleDbContext.Users.Single(user =>
                    user.Id == secondAdministrator.Id).Role);
        }

        using var promoteRequest = CreateJsonRequest(
            HttpMethod.Put,
            $"/api/User/{secondAdministrator.Id}/role",
            administrator.Token,
            new { role = "Administrator" });
        using var promoteResponse = await client.SendAsync(
            promoteRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, promoteResponse.StatusCode);

        using var deleteRequest = CreateRequest(
            HttpMethod.Delete,
            $"/api/User/{secondAdministrator.Id}",
            administrator.Token);
        using var deleteResponse = await client.SendAsync(
            deleteRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(dbContext.Users.Any(user => user.Id == secondAdministrator.Id));
        Assert.Single(dbContext.Users.Where(user => user.Role == "Administrator"));
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
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
        return Assert.IsType<LoginResponse>(login);
    }

    private static async Task<UserResponse> CreateAdministratorAsync(
        HttpClient client,
        string token)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/api/User",
            token,
            new
            {
                userName = $"second-admin-{Guid.NewGuid():N}",
                password = $"secure-{Guid.NewGuid():N}",
                role = "Administrator"
            });
        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<UserResponse>(
            TestContext.Current.CancellationToken);
        return Assert.IsType<UserResponse>(user);
    }

    private static HttpRequestMessage CreateRequest(
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

    private static HttpRequestMessage CreateJsonRequest<T>(
        HttpMethod method,
        string path,
        string token,
        T body)
    {
        var request = CreateRequest(method, path, token);
        request.Content = JsonContent.Create(body);
        return request;
    }
}
