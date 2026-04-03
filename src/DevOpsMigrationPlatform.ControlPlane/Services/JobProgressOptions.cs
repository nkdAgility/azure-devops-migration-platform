using System.ComponentModel.DataAnnotations;

namespace DevOpsMigrationPlatform.ControlPlane.Services;

public sealed class JobProgressOptions
{
    public const string SectionName = "JobProgress";

    [Range(1, 100_000)]
    public int Capacity { get; init; } = 1000;
}
