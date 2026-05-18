// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if NET481
namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

public readonly struct ResumeDecision
{
    public ResumeDecision(bool shouldSkip, string? resumeAtStage)
    {
        ShouldSkip = shouldSkip;
        ResumeAtStage = resumeAtStage;
    }

    public bool ShouldSkip { get; }

    public string? ResumeAtStage { get; }

    public static ResumeDecision Skip { get; } = new(shouldSkip: true, resumeAtStage: null);

    public static ResumeDecision StartFromBeginning { get; } = new(shouldSkip: false, resumeAtStage: null);
}
#endif
