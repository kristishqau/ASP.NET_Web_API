using EmployeeData.Models;
using EmployeeData.Security;
using EmployeeData.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EmployeeData.IntegrationTests;

public sealed class LegacyPasswordMigratorTests
{
    [Fact]
    public void Dry_run_reports_work_without_changing_credentials()
    {
        using var harness = new MigratorHarness();
        var user = harness.AddUser("dry-run-user", "legacy-password");

        var result = harness.Run(LegacyPasswordMigrationMode.DryRun);

        Assert.False(result.AlreadyComplete);
        Assert.Equal(1, result.UsersInspected);
        Assert.Equal(1, result.PasswordsHashed);
        Assert.Equal(1, result.PlaintextValuesCleared);
        var storedUser = harness.GetUser(user.Id);
        Assert.Equal("legacy-password", storedUser.Password);
        Assert.Null(storedUser.PasswordHash);
    }

    [Fact]
    public void Apply_hashes_and_clears_credentials_atomically_and_is_idempotent()
    {
        using var harness = new MigratorHarness();
        const string password = "legacy-password";
        var user = harness.AddUser("apply-user", password);

        var firstResult = harness.Run(LegacyPasswordMigrationMode.Apply);
        var migratedUser = harness.GetUser(user.Id);
        var firstHash = migratedUser.PasswordHash;

        Assert.Equal(1, firstResult.PasswordsHashed);
        Assert.Equal(1, firstResult.PlaintextValuesCleared);
        Assert.Equal(string.Empty, migratedUser.Password);
        Assert.False(string.IsNullOrWhiteSpace(firstHash));
        Assert.Equal(
            PasswordVerificationResult.Success,
            harness.PasswordHasher.VerifyHashedPassword(
                migratedUser,
                firstHash,
                password));

        var secondResult = harness.Run(LegacyPasswordMigrationMode.Apply);
        var rerunUser = harness.GetUser(user.Id);
        Assert.Equal(0, secondResult.PasswordsHashed);
        Assert.Equal(0, secondResult.PlaintextValuesCleared);
        Assert.Equal(firstHash, rerunUser.PasswordHash);
    }

    [Fact]
    public void One_unusable_credential_prevents_every_update()
    {
        using var harness = new MigratorHarness();
        var validUser = harness.AddUser("valid-legacy-user", "valid-password");
        var unusableUser = harness.AddUser("blank-legacy-user", "   ");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            harness.Run(LegacyPasswordMigrationMode.Apply));

        Assert.Contains("neither a usable password hash", exception.Message);
        Assert.Equal("valid-password", harness.GetUser(validUser.Id).Password);
        Assert.Null(harness.GetUser(validUser.Id).PasswordHash);
        Assert.Equal("   ", harness.GetUser(unusableUser.Id).Password);
        Assert.Null(harness.GetUser(unusableUser.Id).PasswordHash);
    }

    [Fact]
    public void Malformed_authoritative_hash_prevents_every_update()
    {
        using var harness = new MigratorHarness();
        var validUser = harness.AddUser("valid-before-malformed", "valid-password");
        var malformedUser = harness.AddUser(
            "malformed-hash-user",
            "residual-plaintext",
            "not-a-password-hash");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            harness.Run(LegacyPasswordMigrationMode.Apply));

        Assert.Contains("blank or malformed", exception.Message);
        Assert.Equal("valid-password", harness.GetUser(validUser.Id).Password);
        Assert.Null(harness.GetUser(validUser.Id).PasswordHash);
        Assert.Equal(
            "not-a-password-hash",
            harness.GetUser(malformedUser.Id).PasswordHash);
    }

    [Fact]
    public void Existing_valid_hash_is_preserved_while_plaintext_is_cleared()
    {
        using var harness = new MigratorHarness();
        var user = new User
        {
            UserName = "already-hashed-user",
            Password = "obsolete-plaintext",
            Role = "Employee"
        };
        user.PasswordHash = harness.PasswordHasher.HashPassword(
            user,
            "current-password");
        var originalHash = user.PasswordHash;
        harness.AddUser(user);

        var result = harness.Run(LegacyPasswordMigrationMode.Apply);
        var storedUser = harness.GetUser(user.Id);

        Assert.Equal(0, result.PasswordsHashed);
        Assert.Equal(1, result.PlaintextValuesCleared);
        Assert.Equal(originalHash, storedUser.PasswordHash);
        Assert.Equal(string.Empty, storedUser.Password);
    }

    [Fact]
    public void Historical_public_administrator_requires_an_explicit_replacement()
    {
        using var harness = new MigratorHarness();
        var administrator = harness.AddUser(
            LegacySeedCredential.UserName,
            "adminpassword",
            role: "Administrator");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            harness.Run(LegacyPasswordMigrationMode.Apply));
        Assert.Contains("historical public administrator", exception.Message);
        Assert.Equal("adminpassword", harness.GetUser(administrator.Id).Password);

        const string replacementPassword = "unique-replacement-password";
        harness.Options = new BootstrapAdminOptions
        {
            RotateLegacySeed = true,
            UserName = LegacySeedCredential.UserName,
            Password = replacementPassword
        };
        harness.Run(LegacyPasswordMigrationMode.Apply);
        var migratedAdministrator = harness.GetUser(administrator.Id);

        Assert.Equal(string.Empty, migratedAdministrator.Password);
        Assert.Equal(
            PasswordVerificationResult.Success,
            harness.PasswordHasher.VerifyHashedPassword(
                migratedAdministrator,
                migratedAdministrator.PasswordHash,
                replacementPassword));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            harness.PasswordHasher.VerifyHashedPassword(
                migratedAdministrator,
                migratedAdministrator.PasswordHash,
                "adminpassword"));
    }

    [Fact]
    public void Schema_without_plaintext_column_is_already_complete()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE [Users] (
                    [Id] INTEGER NOT NULL PRIMARY KEY,
                    [UserName] TEXT NOT NULL,
                    [PasswordHash] TEXT NOT NULL,
                    [Role] TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        using var dbContext = new AppDbContext(options);
        var migrator = new LegacyPasswordMigrator(
            dbContext,
            new PasswordHasher<User>(),
            Options.Create(new BootstrapAdminOptions()),
            NullLogger<LegacyPasswordMigrator>.Instance);

        var result = migrator.Run(LegacyPasswordMigrationMode.Apply);

        Assert.True(result.AlreadyComplete);
        Assert.Equal(0, result.UsersInspected);
    }

    [Fact]
    public void Migration_logs_only_aggregate_counts()
    {
        using var harness = new MigratorHarness();
        const string userName = "private-migration-user";
        const string password = "private-migration-password";
        harness.AddUser(userName, password);
        var sink = new TestLogSink();
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new TestLoggerProvider(sink)));

        harness.Run(
            LegacyPasswordMigrationMode.DryRun,
            loggerFactory.CreateLogger<LegacyPasswordMigrator>());

        var messages = string.Join(Environment.NewLine, sink.Messages);
        Assert.DoesNotContain(userName, messages, StringComparison.Ordinal);
        Assert.DoesNotContain(password, messages, StringComparison.Ordinal);
        Assert.Contains("1 user(s)", messages, StringComparison.Ordinal);
    }

    private sealed class MigratorHarness : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        internal MigratorHarness()
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
        internal PasswordHasher<User> PasswordHasher { get; }
        internal BootstrapAdminOptions Options { get; set; } = new();

        internal LegacyPasswordMigrationResult Run(
            LegacyPasswordMigrationMode mode,
            ILogger<LegacyPasswordMigrator>? logger = null)
        {
            DbContext.ChangeTracker.Clear();
            return new LegacyPasswordMigrator(
                DbContext,
                PasswordHasher,
                Microsoft.Extensions.Options.Options.Create(Options),
                logger ?? NullLogger<LegacyPasswordMigrator>.Instance)
                .Run(mode);
        }

        internal User AddUser(
            string userName,
            string password,
            string? passwordHash = null,
            string role = "Employee")
        {
            return AddUser(new User
            {
                UserName = userName,
                Password = password,
                PasswordHash = passwordHash,
                Role = role
            });
        }

        internal User AddUser(User user)
        {
            DbContext.Users.Add(user);
            DbContext.SaveChanges();
            DbContext.ChangeTracker.Clear();
            return user;
        }

        internal User GetUser(int userId)
        {
            DbContext.ChangeTracker.Clear();
            return DbContext.Users
                .AsNoTracking()
                .Single(user => user.Id == userId);
        }

        public void Dispose()
        {
            DbContext.Dispose();
            _connection.Dispose();
        }
    }
}
