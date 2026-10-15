using EmployeeData.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EmployeeData
{
    public class Program
    {
        private const string LegacyPasswordMigrationPrefix =
            "--legacy-password-migration=";

        public static int Main(string[] args)
        {
            var migrationArguments = args
                .Where(argument => argument.StartsWith(
                    LegacyPasswordMigrationPrefix,
                    StringComparison.Ordinal))
                .ToArray();
            if (migrationArguments.Length > 1)
            {
                Console.Error.WriteLine(
                    "Specify the legacy-password migration mode only once.");
                return 2;
            }

            LegacyPasswordMigrationMode? migrationMode = null;
            if (migrationArguments.Length == 1)
            {
                migrationMode = migrationArguments[0] switch
                {
                    "--legacy-password-migration=dry-run" =>
                        LegacyPasswordMigrationMode.DryRun,
                    "--legacy-password-migration=apply" =>
                        LegacyPasswordMigrationMode.Apply,
                    _ => null
                };

                if (!migrationMode.HasValue)
                {
                    Console.Error.WriteLine(
                        "Legacy-password migration mode must be 'dry-run' or 'apply'.");
                    return 2;
                }
            }

            using var host = CreateHostBuilder(args).Build();
            if (migrationMode.HasValue)
            {
                using var scope = host.Services.CreateScope();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                try
                {
                    scope.ServiceProvider
                        .GetRequiredService<LegacyPasswordMigrator>()
                        .Run(migrationMode.Value);
                    return 0;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        "Legacy-password migration failed: {FailureMessage}",
                        exception.Message);
                    return 1;
                }
            }

            host.Run();
            return 0;
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
