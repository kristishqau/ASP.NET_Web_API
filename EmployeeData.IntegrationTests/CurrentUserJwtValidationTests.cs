using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using EmployeeData.Dto;
using EmployeeData.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using User = EmployeeData.Models.User;

namespace EmployeeData.IntegrationTests;

public sealed class CurrentUserJwtValidationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CurrentUserJwtValidationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task Deleted_users_cannot_continue_using_an_issued_token()
    {
        var user = AddUser("Employee");
        var token = await LoginAsync(user.UserName, user.Password);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storedUser = dbContext.Users.Single(candidate => candidate.Id == user.Id);
            dbContext.Users.Remove(storedUser);
            dbContext.SaveChanges();
        }

        using var response = await SendAuthenticatedRequestAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Role_changes_invalidate_previously_issued_tokens()
    {
        var user = AddUser("Employee");
        var token = await LoginAsync(user.UserName, user.Password);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storedUser = dbContext.Users.Single(candidate => candidate.Id == user.Id);
            storedUser.Role = "Administrator";
            dbContext.SaveChanges();
        }

        using var response = await SendAuthenticatedRequestAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var freshToken = await LoginAsync(user.UserName, user.Password);
        using var freshResponse = await SendAuthenticatedRequestAsync(
            freshToken,
            "/api/User");
        Assert.Equal(HttpStatusCode.OK, freshResponse.StatusCode);
    }

    [Fact]
    public async Task Trusted_tokens_for_unknown_users_are_rejected()
    {
        var token = CreateToken(
            int.MaxValue,
            $"missing-{Guid.NewGuid():N}",
            "Administrator");

        using var response = await SendAuthenticatedRequestAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unsupported_database_roles_fail_closed()
    {
        var user = AddUser("Contractor");
        var token = CreateToken(user.Id, user.UserName, user.Role);

        using var response = await SendAuthenticatedRequestAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private TestUser AddUser(string role)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var password = $"secure-{Guid.NewGuid():N}";
        var user = new User
        {
            UserName = $"jwt-user-{Guid.NewGuid():N}",
            Password = string.Empty,
            Role = role
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        dbContext.Users.Add(user);
        dbContext.SaveChanges();
        return new TestUser(user.Id, user.UserName, password, user.Role);
    }

    private async Task<string> LoginAsync(string userName, string password)
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

    private async Task<HttpResponseMessage> SendAuthenticatedRequestAsync(
        string token,
        string path = "/api/Project")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static string CreateToken(int userId, string userName, string role)
    {
        var signingKey = new SymmetricSecurityKey(ApiWebApplicationFactory.JwtSigningKey)
        {
            KeyId = ApiWebApplicationFactory.JwtKeyId
        };
        var now = DateTime.UtcNow;
        var userIdValue = userId.ToString();
        var token = new JwtSecurityToken(
            issuer: ApiWebApplicationFactory.JwtIssuer,
            audience: ApiWebApplicationFactory.JwtAudience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userIdValue),
                new Claim(ClaimTypes.NameIdentifier, userIdValue),
                new Claim(ClaimTypes.Name, userName),
                new Claim(ClaimTypes.Role, role)
            },
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                signingKey,
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record TestUser(
        int Id,
        string UserName,
        string Password,
        string Role);
}
