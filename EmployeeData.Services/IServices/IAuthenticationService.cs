namespace EmployeeData.Services
{
    public interface IAuthenticationService
    {
        AuthenticationResult Authenticate(string username, string password);
    }
}
