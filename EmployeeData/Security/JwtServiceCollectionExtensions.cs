using EmployeeData.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace EmployeeData.Security
{
    internal static class JwtServiceCollectionExtensions
    {
        public static IServiceCollection AddJwtSecurity(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
            services.AddOptions<JwtOptions>()
                .Bind(configuration.GetSection(JwtOptions.SectionName))
                .ValidateOnStart();

            services.AddSingleton(TimeProvider.System);
            services.AddScoped<IJwtTokenService, JwtTokenService>();
            services.AddScoped<CurrentUserJwtBearerEvents>();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer();

            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptionsAccessor) =>
                {
                    var jwtOptions = jwtOptionsAccessor.Value;

                    bearerOptions.IncludeErrorDetails = false;
                    bearerOptions.MapInboundClaims = false;
                    bearerOptions.EventsType = typeof(CurrentUserJwtBearerEvents);
                    bearerOptions.TokenValidationParameters = new TokenValidationParameters
                    {
                        ClockSkew = TimeSpan.FromSeconds(30),
                        NameClaimType = ClaimTypes.Name,
                        RoleClaimType = ClaimTypes.Role,
                        RequireExpirationTime = true,
                        RequireSignedTokens = true,
                        ValidateAudience = true,
                        ValidateIssuer = true,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = true,
                        ValidAudience = jwtOptions.Audience,
                        ValidIssuer = jwtOptions.Issuer,
                        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                        IssuerSigningKeyResolver = (_, _, keyId, _) =>
                            ResolveSigningKey(jwtOptions, keyId)
                    };
                });

            return services;
        }

        private static IEnumerable<SecurityKey> ResolveSigningKey(JwtOptions options, string keyId)
        {
            if (!string.IsNullOrWhiteSpace(keyId)
                && options.SigningKeys.TryGetValue(keyId, out var encodedKey)
                && JwtSigningKeys.TryCreate(keyId, encodedKey, out var signingKey))
            {
                return new[] { signingKey };
            }

            return Array.Empty<SecurityKey>();
        }
    }
}
