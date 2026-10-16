using EmployeeData.Services;
using EmployeeData.Security;
using EmployeeData.Models;
using EmployeeData.Helper;
using EmployeeData.Health;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace EmployeeData
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(
                    Configuration.GetConnectionString("DefaultConnection"),
                    b => b.MigrationsAssembly("EmployeeData")));

            services.AddProblemDetails();
            services.AddHealthChecks()
                .AddCheck<DatabaseHealthCheck>("database");

            services.AddJwtSecurity(Configuration);
            services.AddOptions<BootstrapAdminOptions>()
                .Bind(Configuration.GetSection(BootstrapAdminOptions.SectionName))
                .ValidateOnStart();
            services.AddSingleton<
                IValidateOptions<BootstrapAdminOptions>,
                BootstrapAdminOptionsValidator>();

            services.AddOptions<LoginRateLimitOptions>()
                .Bind(Configuration.GetSection(LoginRateLimitOptions.SectionName))
                .ValidateOnStart();
            services.AddSingleton<
                IValidateOptions<LoginRateLimitOptions>,
                LoginRateLimitOptionsValidator>();
            var loginRateLimit = Configuration
                .GetSection(LoginRateLimitOptions.SectionName)
                .Get<LoginRateLimitOptions>() ?? new LoginRateLimitOptions();
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = (context, _) =>
                {
                    if (context.Lease.TryGetMetadata(
                        MetadataName.RetryAfter,
                        out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            Math.Ceiling(retryAfter.TotalSeconds)
                                .ToString(CultureInfo.InvariantCulture);
                    }

                    return ValueTask.CompletedTask;
                };
                options.AddPolicy(
                    LoginRateLimitPolicy.Name,
                    httpContext => RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.Connection.RemoteIpAddress?.ToString()
                            ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            PermitLimit = loginRateLimit.PermitLimit,
                            QueueLimit = 0,
                            Window = TimeSpan.FromSeconds(
                                loginRateLimit.WindowSeconds)
                        }));
            });

            services.AddOptions<ApiCorsOptions>()
                .Bind(Configuration.GetSection(ApiCorsOptions.SectionName))
                .ValidateOnStart();
            services.AddSingleton<
                IValidateOptions<ApiCorsOptions>,
                ApiCorsOptionsValidator>();
            var corsOptions = Configuration
                .GetSection(ApiCorsOptions.SectionName)
                .Get<ApiCorsOptions>() ?? new ApiCorsOptions();
            services.AddCors(options =>
            {
                options.AddPolicy(name: ApiCorsPolicy.Name,
                    builder =>
                    {
                        builder.WithOrigins(corsOptions.AllowedOrigins)
                               .AllowAnyHeader()
                               .AllowAnyMethod()
                               .WithExposedHeaders(
                                   PaginationHeaders.PageNumber,
                                   PaginationHeaders.PageSize,
                                   PaginationHeaders.TotalCount);
                    });
            });

            services.AddControllersWithViews();
            services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
            services.AddScoped<AdministratorBootstrapper>();
            services.AddScoped<LegacyPasswordMigrator>();
            services.AddScoped<IAuthenticationService, AuthenticationService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IProjectService, ProjectService>();
            services.AddScoped<ITaskService, TaskService>();

            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });

            services.AddHttpContextAccessor();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "EmployeeData", Version = "v1" });
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    In = ParameterLocation.Header,
                    Name = "Authorization",
                });
                c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                });
            });
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "EmployeeData v1"));
            }
            else
            {
                app.UseExceptionHandler();
                app.UseHsts();
            }

            app.UseStatusCodePages();

            if (!env.IsEnvironment("Testing"))
            {
                ValidateDatabaseMigrations(app);
                BootstrapAdministrator(app);
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseCors(ApiCorsPolicy.Name);
            app.UseRateLimiter();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks(
                    "/health",
                    new HealthCheckOptions
                    {
                        ResponseWriter = WriteHealthResponse
                    })
                    .AllowAnonymous();
            });
        }

        private static System.Threading.Tasks.Task WriteHealthResponse(
            HttpContext context,
            HealthReport report)
        {
            context.Response.Headers.CacheControl = "no-store";
            var response = new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString()
                })
            };

            return context.Response.WriteAsJsonAsync(
                response,
                context.RequestAborted);
        }

        private static void ValidateDatabaseMigrations(IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pendingMigrations = dbContext.Database.GetPendingMigrations().ToArray();

            if (pendingMigrations.Length == 0)
                return;

            throw new InvalidOperationException(
                "The database schema is not current. Apply migrations before starting the API. "
                + $"Pending migrations: {string.Join(", ", pendingMigrations)}. "
                + "For a legacy database created with EnsureCreated, follow the adoption procedure in README.md.");
        }

        private static void BootstrapAdministrator(IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            scope.ServiceProvider.GetRequiredService<AdministratorBootstrapper>().Bootstrap();
        }
    }
}
