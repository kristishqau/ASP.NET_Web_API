namespace EmployeeData.Security
{
    public sealed class BootstrapAdminOptions
    {
        public const string SectionName = "BootstrapAdmin";

        public bool Enabled { get; set; }
        public bool RotateLegacySeed { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
    }
}
