// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited
using System.Collections.Generic;
using System.IO;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
namespace DevOpsMigrationPlatform.Infrastructure.Tests.Services;
public class IdentityMappingContext
{
    public Dictionary<string, string> Mappings { get; } = new();
    public string FallbackIdentity { get; set; } = "migration-bot@target.example.com";
    public string? PackageRoot { get; set; }
    public IPackageAccess? Package { get; set; }
    public FileSystemIdentityMappingService? Sut { get; set; }
    public string? SourceIdentity { get; set; }
    public string? ResolvedIdentity { get; set; }
    // For scenario 3: structural test with mock service
    public Mock<IIdentityTranslationTool> MockIdentityService { get; } = new(MockBehavior.Strict);
    public void BuildSut()
    {
        PackageRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(PackageRoot);
        Package = CreatePackageAccess(PackageRoot, "identity-mapping-tests");
        Sut = new FileSystemIdentityMappingService(Mappings, FallbackIdentity, Package, "test-org", "test-project");
    }
    private static IPackageAccess CreatePackageAccess(string packageRoot, string jobId)
    {
        var packageState = new ActivePackageState
        {
            CurrentPackageUri = $"file:///{packageRoot.Replace(Path.DirectorySeparatorChar, '/')}",
            CurrentJob = new Job
            {
                JobId = jobId,
                Kind = JobKind.Import,
                ConfigPayload = $"{{\"MigrationPlatform\":{{\"Package\":{{\"WorkingDirectory\":\"{packageRoot.Replace("\\", "\\\\")}\"}}}}}}"
            }
        };
        return new ActivePackageAccess(packageState, new PackagePathRouter(), NullLogger<ActivePackageAccess>.Instance);
    }
}
