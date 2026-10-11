using EmployeeData.Security;
using Xunit;

namespace EmployeeData.IntegrationTests;

public sealed class JwtOptionsValidatorTests
{
    [Fact]
    public void Validator_rejects_every_unsafe_configuration_boundary()
    {
        var cases = new[]
        {
            (
                Options: CreateOptions(options => options.Issuer = ""),
                ExpectedFailure: "Jwt:Issuer is required."),
            (
                Options: CreateOptions(options => options.Audience = ""),
                ExpectedFailure: "Jwt:Audience is required."),
            (
                Options: CreateOptions(options => options.AccessTokenMinutes = 0),
                ExpectedFailure: "Jwt:AccessTokenMinutes must be between 1 and 1440."),
            (
                Options: CreateOptions(options => options.AccessTokenMinutes = 1441),
                ExpectedFailure: "Jwt:AccessTokenMinutes must be between 1 and 1440."),
            (
                Options: CreateOptions(options => options.ActiveKeyId = ""),
                ExpectedFailure: "Jwt:ActiveKeyId is required."),
            (
                Options: CreateOptions(options => options.ActiveKeyId = "missing-key"),
                ExpectedFailure: "Jwt:ActiveKeyId must identify an entry in Jwt:SigningKeys."),
            (
                Options: CreateOptions(options =>
                {
                    options.SigningKeys = new Dictionary<string, string>();
                }),
                ExpectedFailure: "Jwt:SigningKeys must contain at least one signing key."),
            (
                Options: CreateOptions(options => options.SigningKeys = null!),
                ExpectedFailure: "Jwt:SigningKeys must contain at least one signing key."),
            (
                Options: CreateOptions(options =>
                {
                    options.SigningKeys = new Dictionary<string, string>
                    {
                        [ApiWebApplicationFactory.JwtKeyId] = "not-base64"
                    };
                }),
                ExpectedFailure: "must be a base64-encoded key of at least 32 bytes."),
            (
                Options: CreateOptions(options =>
                {
                    options.SigningKeys = new Dictionary<string, string>
                    {
                        [ApiWebApplicationFactory.JwtKeyId] =
                            Convert.ToBase64String("too-short"u8)
                    };
                }),
                ExpectedFailure: "must be a base64-encoded key of at least 32 bytes.")
        };
        var validator = new JwtOptionsValidator();

        foreach (var testCase in cases)
        {
            var result = validator.Validate(null!, testCase.Options);

            Assert.True(result.Failed);
            Assert.Contains(
                result.Failures,
                failure => failure.Contains(
                    testCase.ExpectedFailure,
                    StringComparison.Ordinal));
        }

        var validResult = validator.Validate(null!, CreateValidOptions());

        Assert.True(validResult.Succeeded);
    }

    private static JwtOptions CreateValidOptions()
    {
        return new JwtOptions
        {
            Issuer = ApiWebApplicationFactory.JwtIssuer,
            Audience = ApiWebApplicationFactory.JwtAudience,
            AccessTokenMinutes = 60,
            ActiveKeyId = ApiWebApplicationFactory.JwtKeyId,
            SigningKeys = new Dictionary<string, string>
            {
                [ApiWebApplicationFactory.JwtKeyId] =
                    Convert.ToBase64String(ApiWebApplicationFactory.JwtSigningKey)
            }
        };
    }

    private static JwtOptions CreateOptions(Action<JwtOptions> configure)
    {
        var options = CreateValidOptions();
        configure(options);
        return options;
    }
}
