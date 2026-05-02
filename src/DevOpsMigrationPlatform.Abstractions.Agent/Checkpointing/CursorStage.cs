// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;

public static class CursorStage
{
    public const string CreatedOrUpdated = "CreatedOrUpdated";
    public const string AppliedFields = "AppliedFields";
    public const string AppliedLinks = "AppliedLinks";
    public const string UploadedAttachments = "UploadedAttachments";
    public const string Completed = "Completed";
}
