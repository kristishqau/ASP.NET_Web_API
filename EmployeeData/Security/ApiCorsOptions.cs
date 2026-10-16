using System;

namespace EmployeeData.Security
{
    internal sealed class ApiCorsOptions
    {
        internal const string SectionName = "Cors";

        public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    }

    internal static class ApiCorsPolicy
    {
        internal const string Name = "api";
    }
}
