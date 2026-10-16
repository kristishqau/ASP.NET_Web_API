using EmployeeData.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EmployeeData.Health
{
    internal sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<DatabaseHealthCheck> _logger;

        public DatabaseHealthCheck(
            AppDbContext dbContext,
            ILogger<DatabaseHealthCheck> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var canConnect = await _dbContext.Database.CanConnectAsync(
                    cancellationToken);
                return canConnect
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Unhealthy("Database connection is unavailable.");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Database health check failed.");
                return HealthCheckResult.Unhealthy(
                    "Database connection is unavailable.");
            }
        }
    }
}
