using AutoMapper;
using EmployeeData.Dto;
using EmployeeData.Models;

namespace EmployeeData.Helper
{
    public class MappingProfiles : Profile
    {
        public MappingProfiles()
        {
            CreateMap<Project, ProjectDto>();
            CreateMap<Task, TaskDto>();
            CreateMap<User, UserDto>();
            CreateMap<UserDto, User>();
            CreateMap<ProjectDto, Project>();
            CreateMap<TaskDto, Task>();
        }
    }
}
