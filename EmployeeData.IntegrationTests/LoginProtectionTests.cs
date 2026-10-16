using System.Net;
using System.Net.Http.Json;
using EmployeeData.Models;
using EmployeeData.Security;
using EmployeeData.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace EmployeeData.IntegrationTests;

public sealed class LoginProtectionTests
{
    [Fact]
    public async Task Missing_and_known_users_receive_the_same_invalid_login_response()
    {
        using var factory = new ApiWebApplicationFactory();
        using var client = CreateClient(factory);

        using var missingResponse = await client.PostAsJsonAsync(
            "/api/User/login",
            new { username = "missing-user", password = "wrong-password" },
            TestContext.Current.CancellationToken);
        using var knownResponse = await client.PostAsJsonAsync(
            "/api/User/login",
            new
            {
                username = ApiWebApplicationFactory.AdministratorUserName,
                password = "wrong-password"
            },
            TestContext.Current.CancellationToken);

        var missingBody = await missingResponse.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        var knownBody = await knownResponse.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, knownResponse.StatusCode);
        Assert.Equal(missingBody, knownBody);
    }

    [Fact]
    public async Task Login_rate_limit_counts_failed_and_successful_attempts_per_client()
    {
        using var factory = new ApiWebApplicationFactory(
            new Dictionary<string, string?>
            {
                ["LoginRateLimit:PermitLimit"] = "2",
                ["LoginRateLimit:WindowSeconds"] = "60"
            });
        using var client = CreateClient(factory);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var failedResponse = await client.PostAsJsonAsync(
                "/api/User/login",
                new { username = $"missing-{attempt}", password = "wrong-password" },
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, failedResponse.StatusCode);
        }

        using var limitedResponse = await client.PostAsJsonAsync(
            "/api/User/login",
            new
            {
                username = ApiWebApplicationFactory.AdministratorUserName,
                password = ApiWebApplicationFactory.AdministratorPassword
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.NotNull(limitedResponse.Headers.RetryAfter);
        Assert.True(limitedResponse.Headers.RetryAfter.Delta > TimeSpan.Zero);
    }

    [Fact]
    public void Missing_user_still_performs_password_hash_verification()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        using var dbContext = new AppDbContext(dbOptions);
        dbContext.Database.EnsureCreated();
        var passwordHasher = new CountingPasswordHasher();
        var service = new AuthenticationService(
            dbContext,
            new UnexpectedJwtTokenService(),
            passwordHasher,
            NullLogger<AuthenticationService>.Instance);

        var result = service.Authenticate("missing-user", "candidate-password");

        Assert.Null(result);
        Assert.Equal(1, passwordHasher.VerificationCount);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1001, 60)]
    [InlineData(5, 0)]
    [InlineData(5, 3601)]
    public void Validator_rejects_unsafe_rate_limit_boundaries(
        int permitLimit,
        int windowSeconds)
    {
        var result = new LoginRateLimitOptionsValidator().Validate(
            Options.DefaultName,
            new LoginRateLimitOptions
            {
                PermitLimit = permitLimit,
                WindowSeconds = windowSeconds
            });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Startup_rejects_an_invalid_login_rate_limit()
    {
        using var factory = new ApiWebApplicationFactory(
            new Dictionary<string, string?>
            {
                ["LoginRateLimit:PermitLimit"] = "0"
            });

        var exception = Assert.Throws<OptionsValidationException>(factory.CreateClient);

        Assert.Contains("PermitLimit", exception.Message, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(ApiWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    private sealed class CountingPasswordHasher : IPasswordHasher<User>
    {
        internal int VerificationCount { get; private set; }

        public string HashPassword(User user, string password)
        {
            throw new InvalidOperationException("Hashing was not expected.");
        }

        public PasswordVerificationResult VerifyHashedPassword(
            User user,
            string hashedPassword,
            string providedPassword)
        {
            VerificationCount++;
            return PasswordVerificationResult.Failed;
        }
    }

    private sealed class UnexpectedJwtTokenService : IJwtTokenService
    {
        public string CreateToken(User user)
        {
            throw new InvalidOperationException("Token creation was not expected.");
        }
    }
}
