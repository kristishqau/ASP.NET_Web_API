using EmployeeData.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EmployeeData.Security
{
    internal sealed class CurrentUserJwtBearerEvents : JwtBearerEvents
    {
        private const string AdministratorRole = "Administrator";
        private const string EmployeeRole = "Employee";

        private readonly AppDbContext _dbContext;

        public CurrentUserJwtBearerEvents(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public override async Task TokenValidated(TokenValidatedContext context)
        {
            var principal = context.Principal;
            var userIdValue = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var subjectValue = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var userName = principal?.FindFirstValue(ClaimTypes.Name);
            var role = principal?.FindFirstValue(ClaimTypes.Role);

            if (!int.TryParse(userIdValue, out var userId)
                || userId <= 0
                || !string.Equals(userIdValue, subjectValue, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(userName)
                || !IsSupportedRole(role))
            {
                context.Fail("The token does not identify a valid user.");
                return;
            }

            var currentUser = await _dbContext.Users
                .AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new
                {
                    user.UserName,
                    user.Role
                })
                .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

            if (currentUser == null
                || !string.Equals(currentUser.UserName, userName, StringComparison.Ordinal)
                || !string.Equals(currentUser.Role, role, StringComparison.Ordinal))
            {
                context.Fail("The token no longer matches the current user.");
            }
        }

        private static bool IsSupportedRole(string role)
        {
            return string.Equals(role, AdministratorRole, StringComparison.Ordinal)
                || string.Equals(role, EmployeeRole, StringComparison.Ordinal);
        }
    }
}
