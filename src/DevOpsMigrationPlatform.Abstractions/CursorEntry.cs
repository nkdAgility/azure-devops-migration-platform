using System;

namespace DevOpsMigrationPlatform.Abstractions;

public record CursorEntry
{
    public string LastProcessed { get; init; } = string.Empty;
    public string Stage { get; init; } = CursorStage.Completed;
    public DateTimeOffset UpdatedAt { get; init; }
}
