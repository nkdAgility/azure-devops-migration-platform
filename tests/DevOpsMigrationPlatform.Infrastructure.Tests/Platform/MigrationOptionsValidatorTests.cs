using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

/// <summary>
/// Unit tests for the internal branching paths in <see cref="MigrationOptionsValidator"/>.
/// These complement the Reqnroll acceptance tests by covering each individual branch
/// in isolation and providing pinned assertions on the failure message text.
/// </summary>
[TestClass]
public class MigrationOptionsValidatorTests
{
    private static MigrationOptionsValidator Sut() => new();

    private static MigrationOptions ValidExport() => new()
    {
        Mode = "Export",
        Source = new AzureDevOpsEndpointOptions
        {
            Type = "AzureDevOpsServices",
            Url = "https://dev.azure.com/myorg",
            Project = "MyProject"
        },
        Artefacts = new MigrationArtefactsOptions { WorkingDirectory = "D:\\exports\\run-001" }
    };

    // ── Passing cases ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Validate_ValidExportConfig_Succeeds()
    {
        var result = Sut().Validate(null, ValidExport());
        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_ValidImportConfig_Succeeds()
    {
        var opts = new MigrationOptions
        {
            Mode = "Import",
            Target = new AzureDevOpsEndpointOptions
            {
                Type = "AzureDevOpsServices",
                Url = "https://dev.azure.com/targetorg",
                Project = "TargetProject"
            },
            Artefacts = new MigrationArtefactsOptions { WorkingDirectory = "D:\\exports\\run-001" }
        };
        Assert.IsTrue(Sut().Validate(null, opts).Succeeded);
    }

    [TestMethod]
    public void Validate_ValidBothConfig_Succeeds()
    {
        var opts = new MigrationOptions
        {
            Mode = "Both",
            Source = new AzureDevOpsEndpointOptions { Type = "AzureDevOpsServices", Url = "https://dev.azure.com/myorg", Project = "P" },
            Target = new AzureDevOpsEndpointOptions { Type = "AzureDevOpsServices", Url = "https://dev.azure.com/targetorg", Project = "P" },
            Artefacts = new MigrationArtefactsOptions { WorkingDirectory = "D:\\exports" }
        };
        Assert.IsTrue(Sut().Validate(null, opts).Succeeded);
    }

    // ── Mode ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Validate_EmptyMode_FailsWithModeInMessage()
    {
        var opts = ValidExport();
        opts.Mode = "";
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Mode");
    }

    [TestMethod]
    public void Validate_WhitespaceMode_FailsWithModeInMessage()
    {
        var opts = ValidExport();
        opts.Mode = "   ";
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Mode");
    }

    [TestMethod]
    public void Validate_UnrecognisedMode_FailsWithModeInMessage()
    {
        var opts = ValidExport();
        opts.Mode = "Replicate";
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Mode");
    }

    // ── Source / Target ───────────────────────────────────────────────────────

    [TestMethod]
    public void Validate_ExportModeWithoutSource_FailsWithSourceInMessage()
    {
        var opts = ValidExport();
        opts.Source = null;
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Source");
    }

    [TestMethod]
    public void Validate_BothModeWithoutSource_FailsWithSourceInMessage()
    {
        var opts = ValidExport();
        opts.Mode = "Both";
        opts.Target = new AzureDevOpsEndpointOptions { Type = "AzureDevOpsServices", Url = "https://dev.azure.com/t", Project = "T" };
        opts.Source = null;
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Source");
    }

    [TestMethod]
    public void Validate_ImportModeWithoutTarget_FailsWithTargetInMessage()
    {
        var opts = new MigrationOptions
        {
            Mode = "Import",
            Target = null,
            Artefacts = new MigrationArtefactsOptions { WorkingDirectory = "D:\\exports" }
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Target");
    }

    [TestMethod]
    public void Validate_BothModeWithoutTarget_FailsWithTargetInMessage()
    {
        var opts = ValidExport();
        opts.Mode = "Both";
        opts.Target = null;
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Target");
    }

    // ── Artefacts ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Validate_EmptyArtefactsPath_FailsWithArtefactsInMessage()
    {
        var opts = ValidExport();
        opts.Artefacts.WorkingDirectory = "";
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Artefacts");
    }

    [TestMethod]
    public void Validate_WhitespaceArtefactsPath_FailsWithArtefactsInMessage()
    {
        var opts = ValidExport();
        opts.Artefacts.WorkingDirectory = "   ";
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Artefacts");
    }

    // ── Multiple errors reported together ────────────────────────────────────

    [TestMethod]
    public void Validate_MultipleErrors_AllReportedInSingleResult()
    {
        var opts = new MigrationOptions
        {
            Mode = "",
            Artefacts = new MigrationArtefactsOptions { WorkingDirectory = "" }
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Mode");
        StringAssert.Contains(result.FailureMessage, "Artefacts");
    }

    // ── Source type validation ────────────────────────────────────────────────

    [TestMethod]
    public void Validate_SourceTypeAzureDevOpsServices_Succeeds()
    {
        var opts = ValidExport();
        opts.Source!.Type = "AzureDevOpsServices";
        Assert.IsTrue(Sut().Validate(null, opts).Succeeded);
    }

    [TestMethod]
    public void Validate_SourceTypeTeamFoundationServer_Succeeds()
    {
        var opts = ValidExport();
        opts.Source!.Type = "TeamFoundationServer";
        Assert.IsTrue(Sut().Validate(null, opts).Succeeded);
    }

    [TestMethod]
    public void Validate_SourceTypeUnknown_FailsWithSourceTypeInMessage()
    {
        var opts = ValidExport();
        opts.Source!.Type = "MySql";
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Source.Type");
    }

    [TestMethod]
    public void Validate_SourceTypeEmpty_FailsWithSourceTypeInMessage()
    {
        var opts = ValidExport();
        opts.Source!.Type = "";
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Source.Type");
    }

    // ── Target type validation — TFS is never a valid target ─────────────────

    [TestMethod]
    public void Validate_TargetTypeAzureDevOpsServices_Succeeds()
    {
        var opts = new MigrationOptions
        {
            Mode = "Import",
            Target = new AzureDevOpsEndpointOptions
            {
                Type = "AzureDevOpsServices",
                Url = "https://dev.azure.com/targetorg",
                Project = "TargetProject"
            },
            Artefacts = new MigrationArtefactsOptions { WorkingDirectory = "D:\\exports" }
        };
        Assert.IsTrue(Sut().Validate(null, opts).Succeeded);
    }

    [TestMethod]
    public void Validate_TargetTypeTeamFoundationServer_FailsWithTargetTypeInMessage()
    {
        var opts = new MigrationOptions
        {
            Mode = "Import",
            Target = new AzureDevOpsEndpointOptions
            {
                Type = "TeamFoundationServer",
                Url = "http://tfs:8080/tfs/DefaultCollection",
                Project = "MyProject"
            },
            Artefacts = new MigrationArtefactsOptions { WorkingDirectory = "D:\\exports" }
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Target.Type");
        StringAssert.Contains(result.FailureMessage, "TeamFoundationServer");
    }

    [TestMethod]
    public void Validate_TargetTypeUnknown_FailsWithTargetTypeInMessage()
    {
        var opts = new MigrationOptions
        {
            Mode = "Import",
            Target = new AzureDevOpsEndpointOptions
            {
                Type = "GitHub",
                Url = "https://github.com/myorg",
                Project = "MyProject"
            },
            Artefacts = new MigrationArtefactsOptions { WorkingDirectory = "D:\\exports" }
        };
        var result = Sut().Validate(null, opts);
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.FailureMessage, "Target.Type");
    }

    // ── Modules list is optional ──────────────────────────────────────────────

    [TestMethod]
    public void Validate_EmptyModulesList_Succeeds()
    {
        var opts = ValidExport();
        opts.Modules.Clear();
        Assert.IsTrue(Sut().Validate(null, opts).Succeeded);
    }
}

