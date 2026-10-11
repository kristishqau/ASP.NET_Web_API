using EmployeeData.Models;

namespace EmployeeData.Services
{
    public interface IJwtTokenService
    {
        string CreateToken(User user);
    }
}
