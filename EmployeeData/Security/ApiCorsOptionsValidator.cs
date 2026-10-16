using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EmployeeData.Security
{
    internal sealed class ApiCorsOptionsValidator
        : IValidateOptions<ApiCorsOptions>
    {
        public ValidateOptionsResult Validate(string name, ApiCorsOptions options)
        {
            var failures = new List<string>();
            var origins = options.AllowedOrigins ?? Array.Empty<string>();

            if (origins.Length > 20)
                failures.Add("Cors:AllowedOrigins must contain at most 20 origins.");

            foreach (var origin in origins)
            {
                if (!IsExactHttpOrigin(origin))
                {
                    failures.Add(
                        $"Cors origin '{origin}' must be an exact HTTP or HTTPS origin "
                        + "without a wildcard, path, query, fragment, or trailing slash.");
                }
            }

            if (origins
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != origins.Length)
            {
                failures.Add("Cors:AllowedOrigins must not contain duplicates.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }

        private static bool IsExactHttpOrigin(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin)
                || !string.Equals(origin, origin.Trim(), StringComparison.Ordinal)
                || origin.Contains('*')
                || origin.EndsWith("/", StringComparison.Ordinal)
                || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp
                    && uri.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(uri.UserInfo))
            {
                return false;
            }

            return uri.AbsolutePath == "/"
                && string.IsNullOrEmpty(uri.Query)
                && string.IsNullOrEmpty(uri.Fragment);
        }
    }
}
