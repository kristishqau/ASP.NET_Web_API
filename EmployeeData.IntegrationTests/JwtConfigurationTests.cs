using Microsoft.Extensions.Options;
using Xunit;

namespace EmployeeData.IntegrationTests;

public sealed class JwtConfigurationTests
{
    [Fact]
    public void Startup_rejects_a_signing_key_shorter_than_32_bytes()
    {
        using var factory = new ApiWebApplicationFactory(
            new Dictionary<string, string?>
            {
                [$"Jwt:SigningKeys:{ApiWebApplicationFactory.JwtKeyId}"] =
                    Convert.ToBase64String("too-short"u8)
            });

        var exception = Assert.Throws<OptionsValidationException>(factory.CreateClient);

        Assert.Contains("at least 32 bytes", exception.Message, StringComparison.Ordinal);
    }
}
