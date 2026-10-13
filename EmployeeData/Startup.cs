using EmployeeData.Services;
using EmployeeData.Security;
using EmployeeData.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

namespace EmployeeData
{
    public class Startup
    {
        private readonly string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

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

            services.AddJwtSecurity(Configuration);
            services.AddOptions<BootstrapAdminOptions>()
                .Bind(Configuration.GetSection(BootstrapAdminOptions.SectionName))
                .ValidateOnStart();
            services.AddSingleton<
                IValidateOptions<BootstrapAdminOptions>,
                BootstrapAdminOptionsValidator>();

            services.AddCors(options =>
            {
                options.AddPolicy(name: MyAllowSpecificOrigins,
                    builder =>
                    {
                        builder.WithOrigins("http://localhost:8080")
                               .AllowAnyHeader()
                               .AllowAnyMethod();
                    });
            });

            services.AddControllersWithViews();
            services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
            services.AddScoped<AdministratorBootstrapper>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IProjectService, ProjectService>();
            services.AddScoped<ITaskService, TaskService>();

            services.AddAuthorization(options =>
            {
                options.AddPolicy("Administrator", policy => policy.RequireRole("Administrator"));
                options.AddPolicy("Employee", policy => policy.RequireRole("Employee"));
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

            if (!env.IsEnvironment("Testing"))
            {
                ValidateDatabaseMigrations(app);
                BootstrapAdministrator(app);
            }

            // ? Use CORS
            app.UseCors(MyAllowSpecificOrigins);

            app.UseHttpsRedirection();
            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
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
