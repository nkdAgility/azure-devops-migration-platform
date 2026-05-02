// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Services;

public class IdentityMappingContext
{
    public Dictionary<string, string> Mappings { get; } = new();
    public string FallbackIdentity { get; set; } = "migration-bot@target.example.com";
    public string? PackageRoot { get; set; }
    public FileSystemArtefactStore? RealStore { get; set; }
    public FileSystemIdentityMappingService? Sut { get; set; }
    public string? SourceIdentity { get; set; }
    public string? ResolvedIdentity { get; set; }

    // For scenario 3: structural test with mock service
    public Mock<IIdentityLookupTool> MockIdentityService { get; } = new(MockBehavior.Strict);

    public void BuildSut()
    {
        PackageRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(PackageRoot);
        RealStore = new FileSystemArtefactStore(PackageRoot);
        Sut = new FileSystemIdentityMappingService(Mappings, FallbackIdentity, RealStore);
    }
}
