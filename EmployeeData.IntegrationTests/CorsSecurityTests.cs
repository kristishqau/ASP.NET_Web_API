using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Xunit;

namespace EmployeeData.IntegrationTests;

public sealed class CorsSecurityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CorsSecurityTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task Configured_origin_receives_preflight_permission()
    {
        using var request = CreatePreflightRequest("https://client.example");
        using var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal("https://client.example", response.Headers
            .GetValues("Access-Control-Allow-Origin")
            .Single());
        Assert.Contains(
            "POST",
            response.Headers.GetValues("Access-Control-Allow-Methods"));
        Assert.Contains(
            "authorization",
            response.Headers.GetValues("Access-Control-Allow-Headers"),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unconfigured_origin_receives_no_browser_permission()
    {
        using var request = CreatePreflightRequest("https://untrusted.example");
        using var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://client.example/")]
    [InlineData("https://client.example/path")]
    public void Startup_rejects_an_inexact_allowed_origin(string origin)
    {
        using var factory = new ApiWebApplicationFactory(
            new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = origin
            });

        var exception = Assert.Throws<OptionsValidationException>(factory.CreateClient);

        Assert.Contains("exact HTTP or HTTPS origin", exception.Message);
    }

    private static HttpRequestMessage CreatePreflightRequest(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/User/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return request;
    }
}
