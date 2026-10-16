using EmployeeData.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Cryptography;

namespace EmployeeData.Services
{
    public sealed class AuthenticationService : IAuthenticationService
    {
        private static readonly User DummyUser = CreateDummyUser();

        private readonly AppDbContext _dbContext;
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(
            AppDbContext dbContext,
            IJwtTokenService jwtTokenService,
            IPasswordHasher<User> passwordHasher,
            ILogger<AuthenticationService> logger)
        {
            _dbContext = dbContext;
            _jwtTokenService = jwtTokenService;
            _passwordHasher = passwordHasher;
            _logger = logger;
        }

        public AuthenticationResult Authenticate(string username, string password)
        {
            if (password == null)
                return null;

            if (string.IsNullOrEmpty(username))
            {
                VerifyDummyPassword(password);
                return null;
            }

            const int maxVerificationAttempts = 2;
            for (var attempt = 0; attempt < maxVerificationAttempts; attempt++)
            {
                var user = _dbContext.Users
                    .AsNoTracking()
                    .SingleOrDefault(candidate => candidate.UserName == username);

                if (user == null)
                {
                    VerifyDummyPassword(password);
                    return null;
                }

                if (string.IsNullOrWhiteSpace(user.PasswordHash))
                {
                    _logger.LogWarning(
                        "Stored password hash is missing for user {UserId}.",
                        user.Id);
                    VerifyDummyPassword(password);
                    return null;
                }

                PasswordVerificationResult verificationResult;
                try
                {
                    verificationResult = _passwordHasher.VerifyHashedPassword(
                        user,
                        user.PasswordHash,
                        password);
                }
                catch (FormatException)
                {
                    _logger.LogWarning(
                        "Stored password hash is malformed for user {UserId}.",
                        user.Id);
                    VerifyDummyPassword(password);
                    return null;
                }

                if (verificationResult == PasswordVerificationResult.Failed)
                    return null;

                if (verificationResult == PasswordVerificationResult.Success)
                    return CreateResult(user);

                var replacementHash = _passwordHasher.HashPassword(user, password);
                var hashUpdated = _dbContext.Users
                    .Where(candidate =>
                        candidate.Id == user.Id
                        && candidate.PasswordHash == user.PasswordHash)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(candidate => candidate.PasswordHash, replacementHash));

                if (hashUpdated == 0)
                    continue;

                user.PasswordHash = replacementHash;
                return CreateResult(user);
            }

            return null;
        }

        private AuthenticationResult CreateResult(User user)
        {
            return new AuthenticationResult
            {
                Token = _jwtTokenService.CreateToken(user),
                UserId = user.Id,
                UserName = user.UserName,
                Role = user.Role,
                ProfilePictureUrl = user.ProfilePictureUrl
            };
        }

        private void VerifyDummyPassword(string password)
        {
            _passwordHasher.VerifyHashedPassword(
                DummyUser,
                DummyUser.PasswordHash,
                password);
        }

        private static User CreateDummyUser()
        {
            var user = new User
            {
                UserName = "authentication-dummy",
                Role = "Employee",
                PasswordHash = string.Empty
            };
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
            return user;
        }
    }
}
