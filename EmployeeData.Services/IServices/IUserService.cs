using EmployeeData.Models;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace EmployeeData.Services
{
    public interface IUserService
    {
        ICollection<User> GetUsers();
        User GetUser(int userId);
        ICollection<User> GetUsersOfATask(int taskId);
        ICollection<User> GetUsersOfAProject(int projectId);
        bool UserExists(int id);
        bool CreateUser(int? projectId, int? taskId, User user);
        bool SaveUser();
        string Authenticate(string username, string password);
        bool UpdateUser(User user);
        string GetUserRole(int userId);
        int GetCurrentUserId();
        bool AddUserToTaskAndProject(int userId, int? taskId, int? projectId);
        bool RemoveUserFromTaskAndProject(int userId, int? taskId, int? projectId);
        bool DeleteUser(User user);
    }
}
