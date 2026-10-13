using EmployeeData.Dto;
using EmployeeData.Models;
using ProjectTask = EmployeeData.Models.Task;

namespace EmployeeData.Helper
{
    internal static class DtoMappings
    {
        public static ProjectDto ToDto(this Project project) => new()
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description
        };

        public static Project ToEntity(this ProjectDto project) => new()
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description
        };

        public static TaskDto ToDto(this ProjectTask task) => new()
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            IsFinished = task.IsFinished
        };

        public static ProjectTask ToEntity(this TaskDto task) => new()
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            IsFinished = task.IsFinished
        };

        public static UserResponse ToResponse(this User user) => new()
        {
            Id = user.Id,
            UserName = user.UserName,
            Role = user.Role,
            ProfilePictureUrl = user.ProfilePictureUrl
        };

        public static User ToEntity(this CreateUserRequest user) => new()
        {
            UserName = user.UserName,
            Role = user.Role,
            ProfilePictureUrl = user.ProfilePictureUrl
        };
    }
}
