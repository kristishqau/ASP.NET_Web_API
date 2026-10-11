using EmployeeData.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace EmployeeData.IntegrationTests;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    internal const string JwtIssuer = "EmployeeData.IntegrationTests";
    internal const string JwtAudience = "EmployeeData.IntegrationTests.Client";
    internal const string JwtKeyId = "integration-test-key";
    internal const string JwtRetainedKeyId = "integration-test-retained-key";

    internal static readonly byte[] JwtSigningKey =
        Enumerable.Repeat((byte)0x41, 64).ToArray();

    internal static readonly byte[] JwtRetainedSigningKey =
        Enumerable.Repeat((byte)0x43, 64).ToArray();

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;
    private readonly TestLogSink _logSink = new();

    internal IReadOnlyCollection<string> LogMessages => _logSink.Messages;

    public ApiWebApplicationFactory()
        : this(new Dictionary<string, string?>())
    {
    }

    internal ApiWebApplicationFactory(
        IReadOnlyDictionary<string, string?> configurationOverrides)
    {
        _configurationOverrides = configurationOverrides;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(new TestLoggerProvider(_logSink));
        });
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var testConfiguration = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Test-only; replaced by SQLite",
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["Jwt:AccessTokenMinutes"] = "60",
                ["Jwt:ActiveKeyId"] = JwtKeyId,
                [$"Jwt:SigningKeys:{JwtKeyId}"] = Convert.ToBase64String(JwtSigningKey),
                [$"Jwt:SigningKeys:{JwtRetainedKeyId}"] =
                    Convert.ToBase64String(JwtRetainedSigningKey)
            };

            foreach (var setting in _configurationOverrides)
            {
                testConfiguration[setting.Key] = setting.Value;
            }

            configuration.AddInMemoryCollection(testConfiguration);
        });

        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        });
    }

    internal void ClearLogMessages()
    {
        _logSink.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
