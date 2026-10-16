using EmployeeData.Models;
using EmployeeData.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ProjectTask = EmployeeData.Models.Task;

namespace EmployeeData.IntegrationTests;

public sealed class DataIntegrityTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public DataIntegrityTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Task_project_relationship_is_required_and_restrictive()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var taskEntity = Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Metadata.IEntityType>(
            dbContext.Model.FindEntityType(typeof(ProjectTask)));
        var projectId = Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Metadata.IProperty>(
            taskEntity.FindProperty(nameof(ProjectTask.ProjectId)));
        var relationship = Assert.Single(
            taskEntity.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Project));

        Assert.False(projectId.IsNullable);
        Assert.True(relationship.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, relationship.DeleteBehavior);
    }

    [Fact]
    public void Saving_a_task_without_an_existing_project_fails()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Tasks.Add(new ProjectTask
        {
            Title = $"invalid-project-{Guid.NewGuid():N}",
            Description = "The foreign key must be enforced.",
            ProjectId = int.MaxValue
        });

        Assert.Throws<DbUpdateException>(() => dbContext.SaveChanges());
    }

    [Fact]
    public void Domain_strings_are_bounded_and_required_where_expected()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = GetEntityType<User>(dbContext);
        var project = GetEntityType<Project>(dbContext);
        var task = GetEntityType<ProjectTask>(dbContext);
        var designModel = dbContext.GetService<IDesignTimeModel>().Model;
        var designUser = GetEntityType<User>(designModel);
        var designProject = GetEntityType<Project>(designModel);
        var designTask = GetEntityType<ProjectTask>(designModel);

        AssertProperty(user, nameof(User.UserName), isNullable: false, maxLength: 100);
        AssertProperty(user, nameof(User.PasswordHash), isNullable: false, maxLength: 512);
        Assert.Null(user.FindProperty("Password"));
        AssertProperty(user, nameof(User.Role), isNullable: false, maxLength: 32);
        AssertProperty(user, nameof(User.ProfilePictureUrl), isNullable: true, maxLength: 2048);
        AssertProperty(project, nameof(Project.Name), isNullable: false, maxLength: 100);
        AssertProperty(project, nameof(Project.Description), isNullable: true, maxLength: 1000);
        AssertProperty(task, nameof(ProjectTask.Title), isNullable: false, maxLength: 200);
        AssertProperty(task, nameof(ProjectTask.Description), isNullable: true, maxLength: 2000);

        var userNameIndex = Assert.Single(
            user.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(User.UserName) }));
        Assert.True(userNameIndex.IsUnique);

        Assert.Contains(
            designUser.GetCheckConstraints(),
            constraint => constraint.Name == "CK_Users_UserName_NotBlank");
        Assert.Contains(
            designUser.GetCheckConstraints(),
            constraint => constraint.Name == "CK_Users_PasswordHash_NotBlank");
        Assert.Contains(
            designUser.GetCheckConstraints(),
            constraint => constraint.Name == "CK_Users_Role");
        Assert.Contains(
            designProject.GetCheckConstraints(),
            constraint => constraint.Name == "CK_Projects_Name_NotBlank");
        Assert.Contains(
            designTask.GetCheckConstraints(),
            constraint => constraint.Name == "CK_Tasks_Title_NotBlank");
    }

    [Fact]
    public void Duplicate_usernames_are_rejected_by_the_database()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userName = $"duplicate-{Guid.NewGuid():N}";
        dbContext.Users.AddRange(
            NewUser(userName, "Employee"),
            NewUser(userName, "Administrator"));

        Assert.Throws<DbUpdateException>(() => dbContext.SaveChanges());
    }

    [Fact]
    public void Invalid_roles_and_blank_domain_names_are_rejected_by_the_database()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Users.Add(NewUser($"invalid-role-{Guid.NewGuid():N}", "Contractor"));
            Assert.Throws<DbUpdateException>(() => dbContext.SaveChanges());
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Projects.Add(new Project { Name = "   ", Description = "invalid" });
            Assert.Throws<DbUpdateException>(() => dbContext.SaveChanges());
        }
    }

    [Fact]
    public void Blank_password_hashes_are_rejected_by_the_database()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = NewUser($"blank-hash-{Guid.NewGuid():N}", "Employee");
        user.PasswordHash = "   ";
        dbContext.Users.Add(user);

        Assert.Throws<DbUpdateException>(() => dbContext.SaveChanges());
    }

    private static IEntityType GetEntityType<TEntity>(AppDbContext dbContext)
    {
        return Assert.IsAssignableFrom<IEntityType>(
            dbContext.Model.FindEntityType(typeof(TEntity)));
    }

    private static IEntityType GetEntityType<TEntity>(IModel model)
    {
        return Assert.IsAssignableFrom<IEntityType>(
            model.FindEntityType(typeof(TEntity)));
    }

    private static void AssertProperty(
        IEntityType entityType,
        string propertyName,
        bool isNullable,
        int maxLength)
    {
        var property = Assert.IsAssignableFrom<IProperty>(
            entityType.FindProperty(propertyName));
        Assert.Equal(isNullable, property.IsNullable);
        Assert.Equal(maxLength, property.GetMaxLength());
    }

    private static User NewUser(string userName, string role)
    {
        return new User
        {
            UserName = userName,
            PasswordHash = "not-used-by-this-test",
            Role = role
        };
    }
}
