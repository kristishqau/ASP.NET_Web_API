using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EmployeeData.IntegrationTests;

public sealed class AuthenticationBoundaryTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthenticationBoundaryTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task Protected_endpoint_rejects_anonymous_requests()
    {
        using var response = await _client.GetAsync(
            "/api/User",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Nondevelopment_https_responses_include_hsts()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://employee-data.test")
        });

        using var response = await client.GetAsync(
            "/api/User",
            TestContext.Current.CancellationToken);

        Assert.Contains(
            "max-age=",
            response.Headers.GetValues("Strict-Transport-Security").Single());
    }

    [Fact]
    public void Authorization_fallback_requires_an_authenticated_user()
    {
        using var scope = _factory.Services.CreateScope();
        var authorization = scope.ServiceProvider
            .GetRequiredService<IOptions<AuthorizationOptions>>()
            .Value;

        var fallback = Assert.IsType<AuthorizationPolicy>(
            authorization.FallbackPolicy);
        Assert.Contains(
            fallback.Requirements,
            requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task Login_rejects_invalid_credentials_without_echoing_secrets()
    {
        var request = new
        {
            username = "missing-user",
            password = "not-the-password"
        };

        using var response = await _client.PostAsJsonAsync(
            "/api/User/login",
            request,
            TestContext.Current.CancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(request.password, responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\"token\"", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Successful_login_does_not_write_access_token_to_console()
    {
        _factory.ClearLogMessages();
        using var standardOutput = new StringWriter();
        using var standardError = new StringWriter();
        var originalOutput = Console.Out;
        var originalError = Console.Error;

        try
        {
            Console.SetOut(standardOutput);
            Console.SetError(standardError);

            using var response = await _client.PostAsJsonAsync(
                "/api/User/login",
                new
                {
                    username = ApiWebApplicationFactory.AdministratorUserName,
                    password = ApiWebApplicationFactory.AdministratorPassword
                },
                TestContext.Current.CancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var document = JsonDocument.Parse(responseBody);
            var token = document.RootElement.GetProperty("token").GetString();

            Assert.False(string.IsNullOrWhiteSpace(token));
            Assert.DoesNotContain(token, standardOutput.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(token, standardError.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(
                _factory.LogMessages,
                message => message.Contains(token, StringComparison.Ordinal));
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }
}
