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