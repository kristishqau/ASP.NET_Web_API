namespace EmployeeData.Dto
{
    public sealed class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string ProfilePictureUrl { get; set; }
    }
}
