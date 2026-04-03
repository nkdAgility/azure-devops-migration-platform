using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Progress;

internal sealed class JobProgressStoreContext
{
    public const int TestCapacity = 3;

    public JobProgressStore Store { get; }
    public Guid JobId { get; } = new Guid("11111111-1111-1111-1111-111111111111");
    public ProgressEvent? LastAppendedEvent { get; set; }

    public JobProgressStoreContext()
    {
        var options = new Mock<IOptions<JobProgressOptions>>(MockBehavior.Strict);
        options.Setup(o => o.Value).Returns(new JobProgressOptions { Capacity = TestCapacity });
        Store = new JobProgressStore(options.Object);
    }

    public ProgressEvent MakeEvent(string stage) =>
        new ProgressEvent { Module = "Test", Stage = stage };
}
