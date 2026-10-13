using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EmployeeData.Dto;
using EmployeeData.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EmployeeData.IntegrationTests;

public sealed class UserContractTests : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string[] CredentialPropertyNames =
    [
        "password",
        "passwordHash"
    ];

    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UserContractTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task User_responses_never_serialize_credentials()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var userName = $"contract-{Guid.NewGuid():N}";

        using var createRequest = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            "/api/User",
            administrator.Token,
            new
            {
                userName,
                password = "initial-password",
                role = "Employee",
                profilePictureUrl = "https://example.test/avatar.png"
            });
        using var createResponse = await _client.SendAsync(createRequest, cancellationToken);
        var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);
        AssertCredentialFreeJson(createBody);

        using var createdDocument = JsonDocument.Parse(createBody);
        var userId = createdDocument.RootElement.GetProperty("id").GetInt32();

        var (projectId, taskId) = GetSeedMembershipIds();
        var paths = new[]
        {
            "/api/User",
            $"/api/User/{userId}",
            $"/api/User/projects/{projectId}/users",
            $"/api/User/tasks/{taskId}/users"
        };

        foreach (var path in paths)
        {
            using var request = CreateAuthenticatedRequest(HttpMethod.Get, path, administrator.Token);
            using var response = await _client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertCredentialFreeJson(responseBody);
        }
    }

    [Fact]
    public async Task Profile_update_cannot_change_password_or_role()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var userName = $"employee-{Guid.NewGuid():N}";
        const string originalPassword = "original-password";
        const string replacementPassword = "replacement-password";

        var createdUser = await CreateUserAsync(
            administrator.Token,
            userName,
            originalPassword,
            cancellationToken);
        var employee = await LoginAsync(userName, originalPassword);

        using var updateRequest = CreateAuthenticatedJsonRequest(
            HttpMethod.Put,
            $"/api/User/{createdUser.Id}",
            employee.Token,
            new
            {
                userName,
                profilePictureUrl = "https://example.test/updated-avatar.png",
                role = "Administrator",
                password = replacementPassword
            });
        using var updateResponse = await _client.SendAsync(updateRequest, cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        using var employeeRoleRequest = CreateAuthenticatedJsonRequest(
            HttpMethod.Put,
            $"/api/User/{createdUser.Id}/role",
            employee.Token,
            new { role = "Administrator" });
        using var employeeRoleResponse = await _client.SendAsync(
            employeeRoleRequest,
            cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, employeeRoleResponse.StatusCode);

        var unchangedUser = await LoginAsync(userName, originalPassword);
        Assert.Equal("Employee", unchangedUser.Role);

        using var replacedPasswordResponse = await _client.PostAsJsonAsync(
            "/api/User/login",
            new { username = userName, password = replacementPassword },
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replacedPasswordResponse.StatusCode);

        using var invalidRoleRequest = CreateAuthenticatedJsonRequest(
            HttpMethod.Put,
            $"/api/User/{createdUser.Id}/role",
            administrator.Token,
            new { role = "Owner" });
        using var invalidRoleResponse = await _client.SendAsync(invalidRoleRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRoleResponse.StatusCode);

        using var administratorRoleRequest = CreateAuthenticatedJsonRequest(
            HttpMethod.Put,
            $"/api/User/{createdUser.Id}/role",
            administrator.Token,
            new { role = "administrator" });
        using var administratorRoleResponse = await _client.SendAsync(
            administratorRoleRequest,
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, administratorRoleResponse.StatusCode);

        var promotedUser = await LoginAsync(userName, originalPassword);
        Assert.Equal("Administrator", promotedUser.Role);
    }

    [Fact]
    public async Task Employee_cannot_probe_or_update_another_profile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var userName = $"boundary-{Guid.NewGuid():N}";
        var employeeUser = await CreateUserAsync(
            administrator.Token,
            userName,
            "boundary-password",
            cancellationToken);
        var employee = await LoginAsync(userName, "boundary-password");

        var protectedUserIds = new[] { administrator.UserId, int.MaxValue };
        foreach (var protectedUserId in protectedUserIds)
        {
            using var request = CreateAuthenticatedJsonRequest(
                HttpMethod.Put,
                $"/api/User/{protectedUserId}",
                employee.Token,
                new
                {
                    userName = $"forbidden-{employeeUser.Id}",
                    profilePictureUrl = "https://example.test/forbidden.png"
                });
            using var response = await _client.SendAsync(request, cancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    private async Task<LoginResponse> LoginAsync(string userName, string password)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await _client.PostAsJsonAsync(
            "/api/User/login",
            new { username = userName, password },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
        return Assert.IsType<LoginResponse>(login);
    }

    private async Task<UserResponse> CreateUserAsync(
        string administratorToken,
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            "/api/User",
            administratorToken,
            new
            {
                userName,
                password,
                role = "Employee",
                profilePictureUrl = "https://example.test/avatar.png"
            });
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var user = await response.Content.ReadFromJsonAsync<UserResponse>(cancellationToken);
        return Assert.IsType<UserResponse>(user);
    }

    private (int ProjectId, int TaskId) GetSeedMembershipIds()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return (
            dbContext.UserProjects.Select(userProject => userProject.ProjectId).First(),
            dbContext.UserTasks.Select(userTask => userTask.TaskId).First());
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

    private static void AssertCredentialFreeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        AssertCredentialFreeElement(document.RootElement);
    }

    private static void AssertCredentialFreeElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                Assert.False(
                    CredentialPropertyNames.Contains(
                        property.Name,
                        StringComparer.OrdinalIgnoreCase),
                    $"The response exposed credential property '{property.Name}'.");
                AssertCredentialFreeElement(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AssertCredentialFreeElement(item);
            }
        }
    }
}
