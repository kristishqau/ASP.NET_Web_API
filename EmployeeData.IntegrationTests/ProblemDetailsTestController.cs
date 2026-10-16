using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmployeeData.IntegrationTests;

[ApiController]
[Route("__test/problem-details")]
public sealed class ProblemDetailsTestController : ControllerBase
{
    [HttpGet("throw")]
    [AllowAnonymous]
    public IActionResult Throw()
    {
        throw new InvalidOperationException(
            "Sensitive test exception: database-password=super-secret");
    }
}
