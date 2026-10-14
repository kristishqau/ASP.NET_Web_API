using EmployeeData.Models;
using EmployeeData.Services;
using Microsoft.EntityFrameworkCore;
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
}
