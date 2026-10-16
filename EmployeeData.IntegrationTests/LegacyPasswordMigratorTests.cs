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
        var firstHash = Assert.IsType<string>(migratedUser.PasswordHash);

        Assert.Equal(1, firstResult.PasswordsHashed);
        Assert.Equal(1, firstResult.PlaintextValuesCleared);
        Assert.Equal(string.Empty, migratedUser.Password);
        Assert.False(string.IsNullOrWhiteSpace(firstHash));
        Assert.Equal(
            PasswordVerificationResult.Success,
            harness.PasswordHasher.VerifyHashedPassword(
                migratedUser.ToUser(),
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
        var hashingUser = new User
        {
            UserName = "already-hashed-user",
            Role = "Employee",
            PasswordHash = string.Empty
        };
        var originalHash = harness.PasswordHasher.HashPassword(
            hashingUser,
            "current-password");
        var user = harness.AddUser(
            hashingUser.UserName,
            "obsolete-plaintext",
            originalHash);

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
                migratedAdministrator.ToUser(),
                Assert.IsType<string>(migratedAdministrator.PasswordHash),
                replacementPassword));
        Assert.Equal(
            PasswordVerificationResult.Failed,
            harness.PasswordHasher.VerifyHashedPassword(
                migratedAdministrator.ToUser(),
                Assert.IsType<string>(migratedAdministrator.PasswordHash),
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
            using (var command = _connection.CreateCommand())
            {
                command.CommandText =
                    """
                    CREATE TABLE [Users] (
                        [Id] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        [UserName] TEXT NOT NULL,
                        [Password] TEXT NOT NULL,
                        [PasswordHash] TEXT NULL,
                        [Role] TEXT NOT NULL
                    );
                    """;
                command.ExecuteNonQuery();
            }

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;
            DbContext = new AppDbContext(options);
            PasswordHasher = new PasswordHasher<User>();
        }

        internal AppDbContext DbContext { get; }
        internal PasswordHasher<User> PasswordHasher { get; }
        internal BootstrapAdminOptions Options { get; set; } = new();

        internal LegacyPasswordMigrationResult Run(
            LegacyPasswordMigrationMode mode,
            ILogger<LegacyPasswordMigrator>? logger = null)
        {
            return new LegacyPasswordMigrator(
                DbContext,
                PasswordHasher,
                Microsoft.Extensions.Options.Options.Create(Options),
                logger ?? NullLogger<LegacyPasswordMigrator>.Instance)
                .Run(mode);
        }

        internal LegacyUser AddUser(
            string userName,
            string password,
            string? passwordHash = null,
            string role = "Employee")
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO [Users] ([UserName], [Password], [PasswordHash], [Role])
                VALUES (@userName, @password, @passwordHash, @role);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("@userName", userName);
            command.Parameters.AddWithValue("@password", password);
            command.Parameters.AddWithValue(
                "@passwordHash",
                (object?)passwordHash ?? DBNull.Value);
            command.Parameters.AddWithValue("@role", role);
            var id = Convert.ToInt32(command.ExecuteScalar());
            return new LegacyUser(
                id,
                userName,
                password,
                passwordHash,
                role);
        }

        internal LegacyUser GetUser(int userId)
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                """
                SELECT [Id], [UserName], [Password], [PasswordHash], [Role]
                FROM [Users]
                WHERE [Id] = @id;
                """;
            command.Parameters.AddWithValue("@id", userId);
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            return new LegacyUser(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4));
        }

        public void Dispose()
        {
            DbContext.Dispose();
            _connection.Dispose();
        }
    }

    private sealed record LegacyUser(
        int Id,
        string UserName,
        string Password,
        string? PasswordHash,
        string Role)
    {
        internal User ToUser()
        {
            return new User
            {
                Id = Id,
                UserName = UserName,
                PasswordHash = PasswordHash ?? string.Empty,
                Role = Role
            };
        }
    }
}
