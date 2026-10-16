namespace EmployeeData.Security
{
    internal sealed class LoginRateLimitOptions
    {
        internal const string SectionName = "LoginRateLimit";

        public int PermitLimit { get; set; } = 5;
        public int WindowSeconds { get; set; } = 60;
    }

    internal static class LoginRateLimitPolicy
    {
        internal const string Name = "login";
    }
}
