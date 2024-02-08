using EmployeeData.Models;
using System.Threading.Tasks;

namespace EmployeeData.Services.IServices
{
    public interface IAuthenticationService
    {
        Task<(int, string)> Login(string username, string password);
    }
}
