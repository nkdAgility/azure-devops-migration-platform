// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Validation;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

public class PackageValidationContext
{
    public string? PackageRoot { get; set; }
    public FileSystemArtefactStore? RealStore { get; set; }
    public PackageValidator? Sut { get; set; }
    public ValidationResult? LastResult { get; set; }

    // For scenario 4: mock validator returning Failed so orchestrator can react.
    public Mock<IPackageValidator> MockValidator { get; } = new(MockBehavior.Strict);
    public bool ImportPhaseStarted { get; set; }
    public string? JobStatus { get; set; }

    public void CreatePackageRoot()
    {
        PackageRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(PackageRoot);
        RealStore = new FileSystemArtefactStore(PackageRoot);
        Sut = new PackageValidator(RealStore);
    }

    public void WritePackageFile(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(PackageRoot!, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
    }
}
