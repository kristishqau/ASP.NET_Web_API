using EmployeeData.Models;
using EmployeeData.Security;
using EmployeeData.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EmployeeData.IntegrationTests;

public sealed class AdministratorBootstrapperTests
{
    [Fact]
    public void Disabled_bootstrap_does_not_create_an_account()
    {
        using var harness = new BootstrapHarness();

        harness.CreateBootstrapper(new BootstrapAdminOptions()).Bootstrap();

        Assert.Empty(harness.DbContext.Users);
    }

    [Fact]
    public void Enabled_bootstrap_creates_one_hashed_administrator_and_is_idempotent()
    {
        using var harness = new BootstrapHarness();
        const string password = "bootstrap-password";
        var options = new BootstrapAdminOptions
        {
            Enabled = true,
            UserName = " initial-admin ",
            Password = password,
            ProfilePictureUrl = "https://example.test/bootstrap.png"
        };
        var bootstrapper = harness.CreateBootstrapper(options);

        bootstrapper.Bootstrap();
        var originalHash = harness.DbContext.Users.Single().PasswordHash;
        bootstrapper.Bootstrap();

        var administrator = Assert.Single(harness.DbContext.Users.AsNoTracking());
        Assert.Equal("initial-admin", administrator.UserName);
        Assert.Equal("Administrator", administrator.Role);
        Assert.Equal(originalHash, administrator.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            harness.PasswordHasher.VerifyHashedPassword(
                administrator,
                administrator.PasswordHash,
                password));
    }

    [Fact]
    public void Enabled_bootstrap_refuses_to_modify_a_populated_database()
    {
        using var harness = new BootstrapHarness();
        harness.DbContext.Users.Add(new User
        {
            UserName = "existing-user",
            PasswordHash = "not-used-by-this-test",
            Role = "Employee"
        });
        harness.DbContext.SaveChanges();
        var bootstrapper = harness.CreateBootstrapper(new BootstrapAdminOptions
        {
            Enabled = true,
            UserName = "initial-admin",
            Password = "bootstrap-password"
        });

        var exception = Assert.Throws<InvalidOperationException>(bootstrapper.Bootstrap);

        Assert.Contains("already populated", exception.Message, StringComparison.Ordinal);
        var unchangedUser = Assert.Single(harness.DbContext.Users.AsNoTracking());
        Assert.Equal("existing-user", unchangedUser.UserName);
        Assert.Equal("Employee", unchangedUser.Role);
    }

    [Fact]
    public void Enabled_bootstrap_refuses_a_database_with_non_user_data()
    {
        using var harness = new BootstrapHarness();
        harness.DbContext.Projects.Add(new Project
        {
            Name = "existing-project",
            Description = "existing data"
        });
        harness.DbContext.SaveChanges();
        var bootstrapper = harness.CreateBootstrapper(new BootstrapAdminOptions
        {
            Enabled = true,
            UserName = "initial-admin",
            Password = "bootstrap-password"
        });

        var exception = Assert.Throws<InvalidOperationException>(bootstrapper.Bootstrap);

        Assert.Contains("application data", exception.Message, StringComparison.Ordinal);
        Assert.Empty(harness.DbContext.Users);
        Assert.Single(harness.DbContext.Projects.AsNoTracking());
    }

    [Fact]
    public void Legacy_seed_rotation_setting_is_rejected_during_normal_startup()
    {
        using var harness = new BootstrapHarness();
        var bootstrapper = harness.CreateBootstrapper(new BootstrapAdminOptions
        {
            RotateLegacySeed = true,
            UserName = "admin",
            Password = "unique-replacement-password"
        });

        var exception = Assert.Throws<InvalidOperationException>(bootstrapper.Bootstrap);

        Assert.Contains("maintenance-command", exception.Message, StringComparison.Ordinal);
        Assert.Empty(harness.DbContext.Users);
    }

    [Fact]
    public void Enabled_bootstrap_options_require_a_strong_explicit_credential()
    {
        var validator = new BootstrapAdminOptionsValidator();

        var result = validator.Validate(
            Options.DefaultName,
            new BootstrapAdminOptions
            {
                Enabled = true,
                UserName = " ",
                Password = "short"
            });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("UserName", StringComparison.Ordinal));
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Password", StringComparison.Ordinal));
    }

    [Fact]
    public void Bootstrap_options_reject_the_historical_public_password()
    {
        var validator = new BootstrapAdminOptionsValidator();

        var result = validator.Validate(
            Options.DefaultName,
            new BootstrapAdminOptions
            {
                Enabled = true,
                UserName = "initial-admin",
                Password = "adminpassword"
            });

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("historical", StringComparison.Ordinal));
    }

    private sealed class BootstrapHarness : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        internal BootstrapHarness()
        {
            _connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;
            DbContext = new AppDbContext(options);
            DbContext.Database.EnsureCreated();
            PasswordHasher = new PasswordHasher<User>();
        }

        internal AppDbContext DbContext { get; }
        internal IPasswordHasher<User> PasswordHasher { get; }

        internal AdministratorBootstrapper CreateBootstrapper(
            BootstrapAdminOptions options)
        {
            return new AdministratorBootstrapper(
                DbContext,
                PasswordHasher,
                Options.Create(options),
                NullLogger<AdministratorBootstrapper>.Instance);
        }

        public void Dispose()
        {
            DbContext.Dispose();
            _connection.Dispose();
        }
    }
}
