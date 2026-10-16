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
            if (_options.RotateLegacySeed)
            {
                throw new InvalidOperationException(
                    "BootstrapAdmin:RotateLegacySeed is a maintenance-command setting. "
                    + "Run the documented legacy-password migration, then disable it before startup.");
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

    }
}
