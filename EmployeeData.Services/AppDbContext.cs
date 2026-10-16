using EmployeeData.Models;
using Microsoft.EntityFrameworkCore;

namespace EmployeeData.Services
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<Task> Tasks { get; set; }
        public DbSet<UserProject> UserProjects { get; set; }
        public DbSet<UserTask> UserTasks { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(user =>
            {
                user.HasIndex(candidate => candidate.UserName)
                    .IsUnique();
                user.Property(candidate => candidate.ProfilePictureUrl)
                    .IsRequired(false);
                user.ToTable(table =>
                {
                    table.HasCheckConstraint(
                        "CK_Users_UserName_NotBlank",
                        "TRIM([UserName]) <> ''");
                    table.HasCheckConstraint(
                        "CK_Users_PasswordHash_NotBlank",
                        "TRIM([PasswordHash]) <> ''");
                    table.HasCheckConstraint(
                        "CK_Users_Role",
                        "[Role] IN ('Administrator', 'Employee')");
                });
            });

            modelBuilder.Entity<Project>(project =>
            {
                project.Property(candidate => candidate.Description)
                    .IsRequired(false);
                project.ToTable(table => table.HasCheckConstraint(
                    "CK_Projects_Name_NotBlank",
                    "TRIM([Name]) <> ''"));
            });

            modelBuilder.Entity<Task>()
                .HasOne(task => task.Project)
                .WithMany(project => project.Tasks)
                .HasForeignKey(task => task.ProjectId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();
            modelBuilder.Entity<Task>(task =>
            {
                task.Property(candidate => candidate.Description)
                    .IsRequired(false);
                task.ToTable(table => table.HasCheckConstraint(
                    "CK_Tasks_Title_NotBlank",
                    "TRIM([Title]) <> ''"));
            });

            modelBuilder.Entity<UserProject>()
                .HasKey(up => new { up.UserId, up.ProjectId });
            modelBuilder.Entity<UserProject>()
                .HasOne(up => up.User)
                .WithMany(u => u.UserProjects)
                .HasForeignKey(up => up.UserId);
            modelBuilder.Entity<UserProject>()
                .HasOne(up => up.Project)
                .WithMany(p => p.UserProjects)
                .HasForeignKey(up => up.ProjectId);

            modelBuilder.Entity<UserTask>()
                .HasKey(up => new { up.UserId, up.TaskId });
            modelBuilder.Entity<UserTask>()
                .HasOne(up => up.User)
                .WithMany(u => u.UserTasks)
                .HasForeignKey(up => up.UserId);
            modelBuilder.Entity<UserTask>()
                .HasOne(up => up.Task)
                .WithMany(p => p.UserTasks)
                .HasForeignKey(up => up.TaskId);

            base.OnModelCreating(modelBuilder);
        }
    }
}
