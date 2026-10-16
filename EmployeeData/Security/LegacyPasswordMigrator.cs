using EmployeeData.Models;
using EmployeeData.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

namespace EmployeeData.Security
{
    internal enum LegacyPasswordMigrationMode
    {
        DryRun,
        Apply
    }

    internal sealed record LegacyPasswordMigrationResult(
        bool AlreadyComplete,
        int UsersInspected,
        int PasswordsHashed,
        int PlaintextValuesCleared);

    internal sealed class LegacyPasswordMigrator
    {
        private const string SqlServerProvider =
            "Microsoft.EntityFrameworkCore.SqlServer";
        private const string SqliteProvider =
            "Microsoft.EntityFrameworkCore.Sqlite";

        private readonly AppDbContext _dbContext;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly BootstrapAdminOptions _bootstrapOptions;
        private readonly ILogger<LegacyPasswordMigrator> _logger;

        public LegacyPasswordMigrator(
            AppDbContext dbContext,
            IPasswordHasher<User> passwordHasher,
            IOptions<BootstrapAdminOptions> bootstrapOptions,
            ILogger<LegacyPasswordMigrator> logger)
        {
            _dbContext = dbContext;
            _passwordHasher = passwordHasher;
            _bootstrapOptions = bootstrapOptions.Value;
            _logger = logger;
        }

        public LegacyPasswordMigrationResult Run(
            LegacyPasswordMigrationMode mode)
        {
            var connection = _dbContext.Database.GetDbConnection();
            var closeConnection = connection.State != ConnectionState.Open;
            if (closeConnection)
                connection.Open();

            try
            {
                using var transaction = connection.BeginTransaction(
                    IsolationLevel.Serializable);
                var columns = ReadUserColumns(connection, transaction);
                EnsureExpectedSchema(columns);

                if (!columns.Contains("Password"))
                {
                    transaction.Commit();
                    var completed = new LegacyPasswordMigrationResult(
                        AlreadyComplete: true,
                        UsersInspected: 0,
                        PasswordsHashed: 0,
                        PlaintextValuesCleared: 0);
                    LogResult(mode, completed);
                    return completed;
                }

                var plan = BuildPlan(connection, transaction);
                var result = new LegacyPasswordMigrationResult(
                    AlreadyComplete: false,
                    UsersInspected: plan.UsersInspected,
                    PasswordsHashed: plan.PasswordsHashed,
                    PlaintextValuesCleared: plan.Updates.Count);

                if (mode == LegacyPasswordMigrationMode.Apply)
                {
                    ApplyPlan(connection, transaction, plan.Updates);
                    transaction.Commit();
                }

                LogResult(mode, result);
                return result;
            }
            finally
            {
                if (closeConnection)
                    connection.Close();
            }
        }

        private MigrationPlan BuildPlan(
            DbConnection connection,
            DbTransaction transaction)
        {
            var updates = new List<CredentialUpdate>();
            var usersInspected = 0;
            var passwordsHashed = 0;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "SELECT [Id], [UserName], [Role], [Password], [PasswordHash] "
                + "FROM [Users] ORDER BY [Id]";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                usersInspected++;
                var id = reader.GetInt32(0);
                var userName = reader.IsDBNull(1) ? null : reader.GetString(1);
                var role = reader.IsDBNull(2) ? null : reader.GetString(2);
                var plaintext = reader.IsDBNull(3) ? null : reader.GetString(3);
                var passwordHash = reader.IsDBNull(4) ? null : reader.GetString(4);

                if (passwordHash != null)
                {
                    if (!IsSupportedPasswordHash(passwordHash))
                    {
                        throw new InvalidOperationException(
                            "A stored password hash is blank or malformed. "
                            + "Correct the credential data before retrying.");
                    }

                    if (!string.IsNullOrEmpty(plaintext))
                    {
                        updates.Add(new CredentialUpdate(id, passwordHash));
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(plaintext))
                {
                    throw new InvalidOperationException(
                        "A legacy user has neither a usable password hash nor a nonblank "
                        + "plaintext password. Correct the credential data before retrying.");
                }

                var user = new User
                {
                    Id = id,
                    UserName = userName,
                    Role = role,
                    PasswordHash = passwordHash ?? string.Empty
                };
                var passwordToHash = plaintext;
                if (LegacySeedCredential.Matches(userName, role, plaintext))
                {
                    ValidateHistoricalCredentialReplacement();
                    passwordToHash = _bootstrapOptions.Password;
                }

                updates.Add(new CredentialUpdate(
                    id,
                    _passwordHasher.HashPassword(user, passwordToHash)));
                passwordsHashed++;
            }

            return new MigrationPlan(usersInspected, passwordsHashed, updates);
        }

        private static void ApplyPlan(
            DbConnection connection,
            DbTransaction transaction,
            IEnumerable<CredentialUpdate> updates)
        {
            foreach (var update in updates)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "UPDATE [Users] SET [PasswordHash] = @passwordHash, "
                    + "[Password] = '' WHERE [Id] = @id";
                AddParameter(command, "@passwordHash", DbType.String, update.PasswordHash);
                AddParameter(command, "@id", DbType.Int32, update.UserId);

                if (command.ExecuteNonQuery() != 1)
                {
                    throw new InvalidOperationException(
                        "Credential data changed while the migration was running. "
                        + "Stop all application instances and retry.");
                }
            }
        }

        private HashSet<string> ReadUserColumns(
            DbConnection connection,
            DbTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var columnOrdinal = 0;
            switch (_dbContext.Database.ProviderName)
            {
                case SqlServerProvider:
                    command.CommandText =
                        "SELECT [COLUMN_NAME] FROM [INFORMATION_SCHEMA].[COLUMNS] "
                        + "WHERE [TABLE_SCHEMA] = 'dbo' AND [TABLE_NAME] = 'Users'";
                    break;
                case SqliteProvider:
                    command.CommandText = "PRAGMA table_info([Users])";
                    columnOrdinal = 1;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Legacy-password migration supports only SQL Server and SQLite.");
            }

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(columnOrdinal));
            }

            return columns;
        }

        private static void EnsureExpectedSchema(HashSet<string> columns)
        {
            if (!columns.Contains("Id")
                || !columns.Contains("UserName")
                || !columns.Contains("Role")
                || !columns.Contains("PasswordHash"))
            {
                throw new InvalidOperationException(
                    "The Users table is missing required credential columns. "
                    + "Apply migrations through EnforceBoundedDomainValues, then retry.");
            }
        }

        private void ValidateHistoricalCredentialReplacement()
        {
            if (!_bootstrapOptions.RotateLegacySeed
                || !string.Equals(
                    _bootstrapOptions.UserName?.Trim(),
                    LegacySeedCredential.UserName,
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(_bootstrapOptions.Password)
                || _bootstrapOptions.Password.Length < PasswordPolicy.MinimumLength
                || _bootstrapOptions.Password.Length > PasswordPolicy.MaximumLength
                || LegacySeedCredential.PasswordMatches(_bootstrapOptions.Password))
            {
                throw new InvalidOperationException(
                    "The historical public administrator credential requires an explicit "
                    + "validated BootstrapAdmin replacement before migration.");
            }
        }

        private static bool IsSupportedPasswordHash(string passwordHash)
        {
            if (string.IsNullOrWhiteSpace(passwordHash))
                return false;

            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(passwordHash);
            }
            catch (FormatException)
            {
                return false;
            }

            if (payload.Length == 49 && payload[0] == 0x00)
                return true;

            if (payload.Length < 29 || payload[0] != 0x01)
                return false;

            var pseudoRandomFunction = BinaryPrimitives.ReadUInt32BigEndian(
                payload.AsSpan(1, 4));
            var iterationCount = BinaryPrimitives.ReadUInt32BigEndian(
                payload.AsSpan(5, 4));
            var saltLength = BinaryPrimitives.ReadUInt32BigEndian(
                payload.AsSpan(9, 4));
            if (pseudoRandomFunction > 2
                || iterationCount == 0
                || saltLength < 16
                || saltLength > (uint)(payload.Length - 29))
            {
                return false;
            }

            return true;
        }

        private void LogResult(
            LegacyPasswordMigrationMode mode,
            LegacyPasswordMigrationResult result)
        {
            _logger.LogInformation(
                "Legacy-password migration {Mode} inspected {UsersInspected} user(s); "
                + "{PasswordsHashed} password(s) require hashing and "
                + "{PlaintextValuesCleared} plaintext value(s) require clearing. "
                + "Schema already complete: {AlreadyComplete}.",
                mode,
                result.UsersInspected,
                result.PasswordsHashed,
                result.PlaintextValuesCleared,
                result.AlreadyComplete);
        }

        private static void AddParameter(
            DbCommand command,
            string name,
            DbType type,
            object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private sealed record CredentialUpdate(int UserId, string PasswordHash);

        private sealed record MigrationPlan(
            int UsersInspected,
            int PasswordsHashed,
            List<CredentialUpdate> Updates);
    }
}
