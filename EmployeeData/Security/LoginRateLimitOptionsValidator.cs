using Microsoft.Extensions.Options;
using System.Collections.Generic;

namespace EmployeeData.Security
{
    internal sealed class LoginRateLimitOptionsValidator
        : IValidateOptions<LoginRateLimitOptions>
    {
        public ValidateOptionsResult Validate(
            string name,
            LoginRateLimitOptions options)
        {
            var failures = new List<string>();

            if (options.PermitLimit < 1 || options.PermitLimit > 1000)
            {
                failures.Add(
                    "LoginRateLimit:PermitLimit must be between 1 and 1000.");
            }

            if (options.WindowSeconds < 1 || options.WindowSeconds > 3600)
            {
                failures.Add(
                    "LoginRateLimit:WindowSeconds must be between 1 and 3600.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
