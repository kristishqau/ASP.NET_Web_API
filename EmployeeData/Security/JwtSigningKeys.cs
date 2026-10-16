using Microsoft.IdentityModel.Tokens;
using System;
using System.Diagnostics.CodeAnalysis;

namespace EmployeeData.Security
{
    internal static class JwtSigningKeys
    {
        public const int MinimumKeySizeInBytes = 32;

        public static bool TryCreate(
            string? keyId,
            string? encodedKey,
            [NotNullWhen(true)] out SymmetricSecurityKey? signingKey)
        {
            signingKey = null;

            if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(encodedKey))
            {
                return false;
            }

            try
            {
                var keyBytes = Convert.FromBase64String(encodedKey);
                if (keyBytes.Length < MinimumKeySizeInBytes)
                {
                    return false;
                }

                signingKey = new SymmetricSecurityKey(keyBytes)
                {
                    KeyId = keyId
                };
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
