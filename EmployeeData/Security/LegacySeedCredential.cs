using System;
using System.Security.Cryptography;
using System.Text;

namespace EmployeeData.Security
{
    internal static class LegacySeedCredential
    {
        internal const string UserName = "admin";

        private static readonly byte[] PasswordDigest = Convert.FromHexString(
            "749F09BADE8ACA755660EEB17792DA880218D4FBDC4E25FBEC279D7FE9F65D70");

        internal static bool Matches(
            string? userName,
            string? role,
            string? password)
        {
            return string.Equals(userName, UserName, StringComparison.Ordinal)
                && string.Equals(role, "Administrator", StringComparison.Ordinal)
                && PasswordMatches(password);
        }

        internal static bool PasswordMatches(string? password)
        {
            if (password == null)
                return false;

            var candidateDigest = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return CryptographicOperations.FixedTimeEquals(
                PasswordDigest,
                candidateDigest);
        }
    }
}
