using Microsoft.Extensions.Options;
using System.Collections.Generic;

namespace EmployeeData.Security
{
    internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
    {
        public ValidateOptionsResult Validate(string? name, JwtOptions options)
        {
            var failures = new List<string>();

            if (string.IsNullOrWhiteSpace(options.Issuer))
            {
                failures.Add("Jwt:Issuer is required.");
            }

            if (string.IsNullOrWhiteSpace(options.Audience))
            {
                failures.Add("Jwt:Audience is required.");
            }

            if (options.AccessTokenMinutes < 1 || options.AccessTokenMinutes > 1440)
            {
                failures.Add("Jwt:AccessTokenMinutes must be between 1 and 1440.");
            }

            if (options.SigningKeys == null || options.SigningKeys.Count == 0)
            {
                failures.Add("Jwt:SigningKeys must contain at least one signing key.");
            }

            if (string.IsNullOrWhiteSpace(options.ActiveKeyId))
            {
                failures.Add("Jwt:ActiveKeyId is required.");
            }
            else if (options.SigningKeys == null
                || !options.SigningKeys.TryGetValue(options.ActiveKeyId, out _))
            {
                failures.Add("Jwt:ActiveKeyId must identify an entry in Jwt:SigningKeys.");
            }

            foreach (var signingKey in options.SigningKeys ?? new Dictionary<string, string>())
            {
                if (!JwtSigningKeys.TryCreate(signingKey.Key, signingKey.Value, out _))
                {
                    failures.Add(
                        $"Jwt:SigningKeys:{signingKey.Key} must be a base64-encoded key of at least {JwtSigningKeys.MinimumKeySizeInBytes} bytes.");
                }
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
