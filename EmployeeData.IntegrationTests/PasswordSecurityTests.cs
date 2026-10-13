using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EmployeeData.Dto;
using EmployeeData.Models;
using EmployeeData.Security;
using EmployeeData.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace EmployeeData.IntegrationTests;

public sealed class PasswordSecurityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PasswordSecurityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task Api_created_user_is_stored_with_only_a_password_hash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var userName = $"hashed-{Guid.NewGuid():N}";
        const string password = "new-user-password";

        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            "/api/User",
            administrator.Token,
            new
            {
                userName,
                password,
                role = "Employee",
                profilePictureUrl = "https://example.test/hash.png"
            });
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var storedUser = GetStoredUser(userName);
        Assert.Equal(string.Empty, storedUser.Password);
        Assert.False(string.IsNullOrWhiteSpace(storedUser.PasswordHash));
        Assert.DoesNotContain(password, storedUser.PasswordHash, StringComparison.Ordinal);
        AssertPasswordVerifies(storedUser, password);

        var login = await LoginAsync(userName, password);
        Assert.Equal("Employee", login.Role);
    }

    [Fact]
    public async Task Api_rejects_a_new_password_shorter_than_the_policy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var administrator = await LoginAsync(
            ApiWebApplicationFactory.AdministratorUserName,
            ApiWebApplicationFactory.AdministratorPassword);
        var userName = $"short-password-{Guid.NewGuid():N}";

        using var request = CreateAuthenticatedJsonRequest(
            HttpMethod.Post,
            "/api/User",
            administrator.Token,
            new
            {
                userName,
                password = new string('x', PasswordPolicy.MinimumLength - 1),
                role = "Employee"
            });
        using var response = await _client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(FindStoredUser(userName));
    }

    [Fact]
    public async Task Correct_legacy_login_hashes_password_and_clears_plaintext()
    {
        var userName = $"legacy-{Guid.NewGuid():N}";
        const string password = "legacy-password";
        AddUser(new User
        {
            UserName = userName,
            Password = password,
            PasswordHash = null,
            Role = "Employee"
        });

        var login = await LoginAsync(userName, password);
        Assert.Equal("Employee", login.Role);

        var upgradedUser = GetStoredUser(userName);
        Assert.Equal(string.Empty, upgradedUser.Password);
        Assert.False(string.IsNullOrWhiteSpace(upgradedUser.PasswordHash));
        AssertPasswordVerifies(upgradedUser, password);
    }

    [Fact]
    public async Task Wrong_legacy_password_does_not_modify_credentials()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userName = $"legacy-failed-{Guid.NewGuid():N}";
        const string storedPassword = "stored-password";
        AddUser(new User
        {
            UserName = userName,
            Password = storedPassword,
            PasswordHash = null,
            Role = "Employee"
        });

        using var response = await _client.PostAsJsonAsync(
            "/api/User/login",
            new { username = userName, password = "wrong-password" },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var unchangedUser = GetStoredUser(userName);
        Assert.Equal(storedPassword, unchangedUser.Password);
        Assert.Null(unchangedUser.PasswordHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-password-hash")]
    public async Task Non_null_invalid_hash_never_falls_back_to_plaintext(string invalidHash)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userName = $"invalid-hash-{Guid.NewGuid():N}";
        const string residualPlaintext = "residual-password";
        AddUser(new User
        {
            UserName = userName,
            Password = residualPlaintext,
            PasswordHash = invalidHash,
            Role = "Employee"
        });

        using var response = await _client.PostAsJsonAsync(
            "/api/User/login",
            new { username = userName, password = residualPlaintext },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var unchangedUser = GetStoredUser(userName);
        Assert.Equal(residualPlaintext, unchangedUser.Password);
        Assert.Equal(invalidHash, unchangedUser.PasswordHash);
    }

    [Fact]
    public async Task Valid_hash_mismatch_never_falls_back_to_plaintext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userName = $"hash-mismatch-{Guid.NewGuid():N}";
        const string residualPlaintext = "residual-password";
        var user = new User
        {
            UserName = userName,
            Password = residualPlaintext,
            Role = "Employee"
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var passwordHasher = scope.ServiceProvider
                .GetRequiredService<IPasswordHasher<User>>();
            user.PasswordHash = passwordHasher.HashPassword(
                user,
                "different-current-password");
        }
        var originalHash = user.PasswordHash;
        AddUser(user);

        using var response = await _client.PostAsJsonAsync(
            "/api/User/login",
            new { username = userName, password = residualPlaintext },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var unchangedUser = GetStoredUser(userName);
        Assert.Equal(residualPlaintext, unchangedUser.Password);
        Assert.Equal(originalHash, unchangedUser.PasswordHash);
    }

    [Fact]
    public async Task Valid_hash_login_clears_residual_plaintext()
    {
        var userName = $"hash-cleanup-{Guid.NewGuid():N}";
        const string currentPassword = "current-password-value";
        var user = new User
        {
            UserName = userName,
            Password = "obsolete-plaintext",
            Role = "Employee"
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var passwordHasher = scope.ServiceProvider
                .GetRequiredService<IPasswordHasher<User>>();
            user.PasswordHash = passwordHasher.HashPassword(user, currentPassword);
        }
        var originalHash = user.PasswordHash;
        AddUser(user);

        await LoginAsync(userName, currentPassword);

        var cleanedUser = GetStoredUser(userName);
        Assert.Equal(string.Empty, cleanedUser.Password);
        Assert.Equal(originalHash, cleanedUser.PasswordHash);
    }

    [Fact]
    public async Task Login_rehashes_an_outdated_hash_before_issuing_a_token()
    {
        var userName = $"rehash-{Guid.NewGuid():N}";
        const string password = "rehash-password";
        var user = new User
        {
            UserName = userName,
            Password = string.Empty,
            Role = "Employee"
        };
        var legacyHasher = new PasswordHasher<User>(Options.Create(
            new PasswordHasherOptions { IterationCount = 10_000 }));
        user.PasswordHash = legacyHasher.HashPassword(user, password);
        var originalHash = user.PasswordHash;
        AddUser(user);

        await LoginAsync(userName, password);

        var rehashedUser = GetStoredUser(userName);
        Assert.NotEqual(originalHash, rehashedUser.PasswordHash);
        Assert.Equal(string.Empty, rehashedUser.Password);
        AssertPasswordVerifies(rehashedUser, password);
    }

    [Fact]
    public async Task Concurrent_legacy_logins_converge_on_one_valid_hash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userName = $"concurrent-{Guid.NewGuid():N}";
        const string password = "concurrent-password";
        AddUser(new User
        {
            UserName = userName,
            Password = password,
            PasswordHash = null,
            Role = "Employee"
        });

        var loginTasks = Enumerable.Range(0, 2)
            .Select(_ => _client.PostAsJsonAsync(
                "/api/User/login",
                new { username = userName, password },
                cancellationToken))
            .ToArray();
        var responses = await Task.WhenAll(loginTasks);

        try
        {
            Assert.All(
                responses,
                response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var upgradedUser = GetStoredUser(userName);
        Assert.Equal(string.Empty, upgradedUser.Password);
        AssertPasswordVerifies(upgradedUser, password);
    }

    private async Task<LoginResponse> LoginAsync(string userName, string password)
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

    private void AddUser(User user)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Users.Add(user);
        dbContext.SaveChanges();
    }

    private User GetStoredUser(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.Users
            .AsNoTracking()
            .Single(user => user.UserName == userName);
    }

    private User? FindStoredUser(string userName)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.Users
            .AsNoTracking()
            .SingleOrDefault(user => user.UserName == userName);
    }

    private void AssertPasswordVerifies(User user, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var passwordHasher = scope.ServiceProvider
            .GetRequiredService<IPasswordHasher<User>>();
        var result = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            password);

        Assert.NotEqual(PasswordVerificationResult.Failed, result);
    }

    private static HttpRequestMessage CreateAuthenticatedJsonRequest<T>(
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
