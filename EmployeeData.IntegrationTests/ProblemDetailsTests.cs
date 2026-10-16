using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EmployeeData.Dto;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EmployeeData.IntegrationTests;

public sealed class ProblemDetailsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ProblemDetailsTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task Automatic_validation_uses_problem_details()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/User/login",
            new { },
            TestContext.Current.CancellationToken);

        await AssertProblemDetails(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Authentication_and_authorization_failures_use_problem_details()
    {
        using var anonymousResponse = await _client.GetAsync(
            "/api/User",
            TestContext.Current.CancellationToken);
        await AssertProblemDetails(anonymousResponse, HttpStatusCode.Unauthorized);

        var administrator = await Login(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var userName = $"problem-employee-{Guid.NewGuid():N}";
        const string password = "problem-employee-password";
        using var createRequest = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            "/api/User",
            administrator.Token,
            new { userName, password, role = "Employee" });
        using var createResponse = await _client.SendAsync(
            createRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var employee = await Login(userName, password);
        using var forbiddenRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/User",
            employee.Token);
        using var forbiddenResponse = await _client.SendAsync(
            forbiddenRequest,
            TestContext.Current.CancellationToken);

        await AssertProblemDetails(forbiddenResponse, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Not_found_and_business_validation_use_problem_details()
    {
        var administrator = await Login(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        using var missingRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/User/{int.MaxValue}",
            administrator.Token);
        using var missingResponse = await _client.SendAsync(
            missingRequest,
            TestContext.Current.CancellationToken);
        await AssertProblemDetails(missingResponse, HttpStatusCode.NotFound);

        using var duplicateRequest = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            "/api/User",
            administrator.Token,
            new
            {
                userName = ApiWebApplicationFactory.AdministratorUserName,
                password = "duplicate-user-password",
                role = "Employee"
            });
        using var duplicateResponse = await _client.SendAsync(
            duplicateRequest,
            TestContext.Current.CancellationToken);

        await AssertProblemDetails(
            duplicateResponse,
            HttpStatusCode.UnprocessableEntity,
            expectsErrors: true);
    }

    [Fact]
    public async Task Unexpected_exceptions_return_sanitized_problem_details()
    {
        using var response = await _client.GetAsync(
            "/__test/problem-details/throw",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        await AssertProblemDetails(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("database-password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<LoginResponse> Login(string userName, string password)
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

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string path,
        string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

    private static async System.Threading.Tasks.Task AssertProblemDetails(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        bool expectsErrors = false)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        Assert.Equal((int)expectedStatus, document.RootElement
            .GetProperty("status")
            .GetInt32());
        Assert.True(document.RootElement.TryGetProperty("title", out _));
        if (expectsErrors)
            Assert.True(document.RootElement.TryGetProperty("errors", out _));
    }
}
