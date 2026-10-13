using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace EmployeeData.IntegrationTests;

public sealed class JwtAuthenticationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public JwtAuthenticationTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task Issued_token_contains_expected_metadata_and_authenticates()
    {
        var encodedToken = await LoginAsync();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(encodedToken);

        Assert.Equal(ApiWebApplicationFactory.JwtKeyId, token.Header.Kid);
        Assert.Equal(ApiWebApplicationFactory.JwtIssuer, token.Issuer);
        Assert.Contains(ApiWebApplicationFactory.JwtAudience, token.Audiences);
        Assert.Contains(token.Claims, claim =>
            claim.Type == JwtRegisteredClaimNames.Jti
            && !string.IsNullOrWhiteSpace(claim.Value));

        using var request = CreateAuthenticatedRequest(encodedToken);
        using var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var projectsRequest = CreateAuthenticatedRequest(
            encodedToken,
            "/api/Project/GetProjectsForUser");
        using var projectsResponse = await _client.SendAsync(
            projectsRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, projectsResponse.StatusCode);
    }

    [Fact]
    public async Task Retained_signing_key_validates_during_rotation_overlap()
    {
        var now = DateTime.UtcNow;
        var token = CreateToken(
            ApiWebApplicationFactory.JwtRetainedSigningKey,
            ApiWebApplicationFactory.JwtRetainedKeyId,
            ApiWebApplicationFactory.JwtIssuer,
            ApiWebApplicationFactory.JwtAudience,
            now.AddMinutes(-1),
            now.AddMinutes(5),
            SecurityAlgorithms.HmacSha256);

        using var request = CreateAuthenticatedRequest(token);
        using var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(TokenFault.WrongSignature)]
    [InlineData(TokenFault.WrongIssuer)]
    [InlineData(TokenFault.WrongAudience)]
    [InlineData(TokenFault.Expired)]
    [InlineData(TokenFault.UnknownKey)]
    [InlineData(TokenFault.WrongAlgorithm)]
    [InlineData(TokenFault.MissingKeyId)]
    [InlineData(TokenFault.MissingExpiration)]
    [InlineData(TokenFault.NotYetValid)]
    public async Task Invalid_tokens_are_rejected(TokenFault fault)
    {
        using var request = CreateAuthenticatedRequest(CreateInvalidToken(fault));
        using var response = await _client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> LoginAsync()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/User/login",
            new
            {
                username = ApiWebApplicationFactory.AdministratorUserName,
                password = ApiWebApplicationFactory.AdministratorPassword
            },
            TestContext.Current.CancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement.GetProperty("token").GetString()!;
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        string token,
        string path = "/api/User")
    {
        return new HttpRequestMessage(HttpMethod.Get, path)
        {
            Headers =
            {
                Authorization = new AuthenticationHeaderValue("Bearer", token)
            }
        };
    }

    private static string CreateInvalidToken(TokenFault fault)
    {
        var signingKeyBytes = fault switch
        {
            TokenFault.WrongSignature =>
                Enumerable.Repeat((byte)0x42, 64).ToArray(),
            _ => ApiWebApplicationFactory.JwtSigningKey
        };
        var now = DateTime.UtcNow;
        return CreateToken(
            signingKeyBytes,
            fault switch
            {
                TokenFault.UnknownKey => "removed-integration-test-key",
                TokenFault.MissingKeyId => null,
                _ => ApiWebApplicationFactory.JwtKeyId
            },
            fault == TokenFault.WrongIssuer
                ? "Unexpected.Issuer"
                : ApiWebApplicationFactory.JwtIssuer,
            fault == TokenFault.WrongAudience
                ? "Unexpected.Audience"
                : ApiWebApplicationFactory.JwtAudience,
            fault == TokenFault.NotYetValid
                ? now.AddMinutes(5)
                : now.AddMinutes(-10),
            fault switch
            {
                TokenFault.Expired => now.AddMinutes(-5),
                TokenFault.MissingExpiration => null,
                _ => now.AddMinutes(10)
            },
            fault == TokenFault.WrongAlgorithm
                ? SecurityAlgorithms.HmacSha512
                : SecurityAlgorithms.HmacSha256);
    }

    private static string CreateToken(
        byte[] signingKeyBytes,
        string? keyId,
        string issuer,
        string audience,
        DateTime notBefore,
        DateTime? expires,
        string algorithm)
    {
        var signingKey = new SymmetricSecurityKey(signingKeyBytes)
        {
            KeyId = keyId
        };
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, "Administrator")
            },
            notBefore: notBefore,
            expires: expires,
            signingCredentials: new SigningCredentials(signingKey, algorithm));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public enum TokenFault
    {
        WrongSignature,
        WrongIssuer,
        WrongAudience,
        Expired,
        UnknownKey,
        WrongAlgorithm,
        MissingKeyId,
        MissingExpiration,
        NotYetValid
    }
}
