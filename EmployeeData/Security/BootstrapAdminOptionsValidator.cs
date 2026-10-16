using Microsoft.Extensions.Options;
using System.Collections.Generic;

namespace EmployeeData.Security
{
    internal sealed class BootstrapAdminOptionsValidator
        : IValidateOptions<BootstrapAdminOptions>
    {
        public ValidateOptionsResult Validate(
            string? name,
            BootstrapAdminOptions options)
        {
            if (!options.Enabled && !options.RotateLegacySeed)
                return ValidateOptionsResult.Success;

            var failures = new List<string>();
            var userName = options.UserName?.Trim();

            if (options.Enabled && options.RotateLegacySeed)
            {
                failures.Add(
                    "BootstrapAdmin:Enabled and BootstrapAdmin:RotateLegacySeed are mutually exclusive.");
            }

            if (string.IsNullOrWhiteSpace(userName))
                failures.Add(
                    "BootstrapAdmin:UserName is required when bootstrap or legacy-seed rotation is enabled.");
            else if (userName.Length > 100)
                failures.Add("BootstrapAdmin:UserName must not exceed 100 characters.");

            if (string.IsNullOrWhiteSpace(options.Password)
                || options.Password.Length < PasswordPolicy.MinimumLength
                || options.Password.Length > PasswordPolicy.MaximumLength)
            {
                failures.Add(
                    $"BootstrapAdmin:Password must be non-blank and contain between "
                    + $"{PasswordPolicy.MinimumLength} and {PasswordPolicy.MaximumLength} characters.");
            }
            if (LegacySeedCredential.PasswordMatches(options.Password))
            {
                failures.Add(
                    "BootstrapAdmin:Password must not reuse the historical demonstration password.");
            }

            if (options.RotateLegacySeed
                && !string.Equals(
                    userName,
                    LegacySeedCredential.UserName,
                    System.StringComparison.Ordinal))
            {
                failures.Add(
                    $"BootstrapAdmin:UserName must be '{LegacySeedCredential.UserName}' when rotating the historical seed.");
            }

            if (options.ProfilePictureUrl?.Length > 2048)
            {
                failures.Add(
                    "BootstrapAdmin:ProfilePictureUrl must not exceed 2048 characters.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
