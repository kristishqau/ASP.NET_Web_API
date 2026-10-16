namespace EmployeeData.Services
{
    public sealed class AuthenticationResult
    {
        public string Token { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public string Role { get; set; }
        public string ProfilePictureUrl { get; set; }
    }
}
