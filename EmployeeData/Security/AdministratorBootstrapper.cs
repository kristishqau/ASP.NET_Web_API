using EmployeeData.Models;
using EmployeeData.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Data;
using System.Linq;

namespace EmployeeData.Security
{
    internal sealed class AdministratorBootstrapper
    {
        private const string AdministratorRole = "Administrator";

        private readonly AppDbContext _dbContext;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly BootstrapAdminOptions _options;
        private readonly ILogger<AdministratorBootstrapper> _logger;

        public AdministratorBootstrapper(
            AppDbContext dbContext,
            IPasswordHasher<User> passwordHasher,
            IOptions<BootstrapAdminOptions> options,
            ILogger<AdministratorBootstrapper> logger)
        {
            _dbContext = dbContext;
            _passwordHasher = passwordHasher;
            _options = options.Value;
            _logger = logger;
        }

        public void Bootstrap()
        {
            using var transaction = _dbContext.Database.BeginTransaction(IsolationLevel.Serializable);
            var legacySeedAdministrator = _dbContext.Users
                .Where(user =>
                    user.UserName == LegacySeedCredential.UserName
                    && user.Role == AdministratorRole
                    && user.PasswordHash == null)
                .AsEnumerable()
                .SingleOrDefault(LegacySeedCredential.Matches);

            if (legacySeedAdministrator != null)
            {
                if (!_options.RotateLegacySeed)
                {
                    throw new InvalidOperationException(
                        "The historical demonstration administrator is still using its public credential. "
                        + "Complete the documented BootstrapAdmin legacy-seed rotation before startup.");
                }

                legacySeedAdministrator.PasswordHash = _passwordHasher.HashPassword(
                    legacySeedAdministrator,
                    _options.Password);
                legacySeedAdministrator.Password = string.Empty;
                _dbContext.SaveChanges();
                transaction.Commit();

                _logger.LogInformation(
                    "Rotated the historical demonstration administrator credential for user {UserId}.",
                    legacySeedAdministrator.Id);
                return;
            }

            if (_options.RotateLegacySeed)
            {
                VerifyCompletedLegacySeedRotation(transaction);
                return;
            }

            if (!_options.Enabled)
            {
                transaction.Commit();
                return;
            }

            var userName = _options.UserName.Trim();

            if (_dbContext.Users.Any())
            {
                var existingUser = _dbContext.Users
                    .SingleOrDefault(user => user.UserName == userName);

                if (existingUser != null && existingUser.Role == AdministratorRole)
                {
                    _logger.LogInformation(
                        "Administrator bootstrap skipped because configured user {UserId} already exists.",
                        existingUser.Id);
                    transaction.Commit();
                    return;
                }

                throw new InvalidOperationException(
                    "Administrator bootstrap is enabled, but the database is already populated "
                    + "and does not contain the configured administrator. No account was changed.");
            }

            if (_dbContext.Projects.Any()
                || _dbContext.Tasks.Any()
                || _dbContext.UserProjects.Any()
                || _dbContext.UserTasks.Any())
            {
                throw new InvalidOperationException(
                    "Administrator bootstrap is enabled, but the database contains application data. "
                    + "No account was created.");
            }

            var administrator = new User
            {
                UserName = userName,
                Password = string.Empty,
                Role = AdministratorRole,
                ProfilePictureUrl = _options.ProfilePictureUrl
            };
            administrator.PasswordHash = _passwordHasher.HashPassword(
                administrator,
                _options.Password);

            _dbContext.Users.Add(administrator);
            _dbContext.SaveChanges();
            transaction.Commit();

            _logger.LogInformation(
                "Created bootstrap administrator {UserId}. Disable BootstrapAdmin after first use.",
                administrator.Id);
        }

        private void VerifyCompletedLegacySeedRotation(
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        {
            var existingAdministrator = _dbContext.Users.SingleOrDefault(user =>
                user.UserName == LegacySeedCredential.UserName
                && user.Role == AdministratorRole);

            if (existingAdministrator?.PasswordHash == null)
            {
                throw new InvalidOperationException(
                    "Legacy-seed rotation was requested, but the exact historical credential was not found. "
                    + "No account was changed.");
            }

            PasswordVerificationResult verificationResult;
            try
            {
                verificationResult = _passwordHasher.VerifyHashedPassword(
                    existingAdministrator,
                    existingAdministrator.PasswordHash,
                    _options.Password);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "Legacy-seed rotation cannot verify the stored administrator credential.",
                    exception);
            }

            if (verificationResult == PasswordVerificationResult.Failed)
            {
                throw new InvalidOperationException(
                    "Legacy-seed rotation was already completed with a different credential. "
                    + "No account was changed.");
            }

            transaction.Commit();
            _logger.LogInformation(
                "Historical administrator credential rotation is already complete for user {UserId}.",
                existingAdministrator.Id);
        }
    }
}
