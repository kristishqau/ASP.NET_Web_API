using System.Collections.Generic;

namespace EmployeeData.Security
{
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int AccessTokenMinutes { get; set; } = 60;
        public string ActiveKeyId { get; set; } = string.Empty;
        public Dictionary<string, string> SigningKeys { get; set; } = new Dictionary<string, string>();
    }
}
