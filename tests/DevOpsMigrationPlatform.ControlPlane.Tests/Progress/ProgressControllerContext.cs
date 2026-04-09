using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Controllers;
using DevOpsMigrationPlatform.ControlPlane.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Progress;

internal sealed class ProgressControllerContext
{
    public const int TestCapacity = 5;

    public JobProgressStore Store { get; }
    public Mock<ILeaseJobResolver> LeaseResolver { get; } = new(MockBehavior.Strict);
    public ProgressController Controller { get; }

    public ProgressControllerContext()
    {
        var options = new Mock<IOptions<JobProgressOptions>>(MockBehavior.Strict);
        options.Setup(o => o.Value).Returns(new JobProgressOptions { Capacity = TestCapacity });
        Store = new JobProgressStore(options.Object);

        var jobStore = new JobStore();
        Controller = new ProgressController(
            Store,
            jobStore,
            LeaseResolver.Object,
            NullLogger<ProgressController>.Instance);
    }

    public void SetAuthenticatedUser()
    {
        var identity = new ClaimsIdentity("TestAuth");
        identity.AddClaim(new Claim(ClaimTypes.Name, "test-user"));
        var principal = new ClaimsPrincipal(identity);

        Controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = principal,
                Response = { Body = new System.IO.MemoryStream() }
            }
        };
    }

    public void SetUnauthenticatedUser()
    {
        Controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Response = { Body = new System.IO.MemoryStream() }
            }
        };
    }

    public ProgressEvent MakeEvent(string stage) =>
        new ProgressEvent { Module = "Test", Stage = stage };
}
