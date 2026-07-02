// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

// Companion stubs used only by TfsExportBuilder.
// These are internal implementation details of the builder, not DSL surface.

using DevOpsMigrationPlatform.CLI.Migration.Commands;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.TfsExport;

// NOTE: ITfsJobServiceFactory is defined in DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.
// The CLI Migration test project does not currently reference that assembly.
// ThrowingTfsJobServiceFactory is retained as a placeholder; wire it up once the
// TfsObjectModel project reference is added to DevOpsMigrationPlatform.CLI.Migration.Tests.csproj.
//
// To activate: add the project reference and uncomment the implementation below.

/*
using DevOpsMigrationPlatform.Abstractions.Agent.TfsExecution;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.JobLifecycle.TfsExecution;
using DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Stub <c>ITfsJobServiceFactory</c> that always throws on <c>CreateForEndpoint</c>,
/// simulating TFS export being unavailable.
/// </summary>
internal sealed class ThrowingTfsJobServiceFactory : ITfsJobServiceFactory
{
    public ITfsJobServices CreateForEndpoint(MigrationEndpointOptions endpoint)
        => throw new InvalidOperationException(
               "TFS export service is not available for this endpoint.");
}
*/

/// <summary>
/// Stub that forces the subprocess runner to return a fixed exit code.
/// Used by scenario 5 (exit-code propagation) via <c>TfsExportBuilder.RunInProcessAsync</c>.
/// Registered in the test host's DI container so <see cref="QueueCommand"/> can
/// propagate it as the CLI exit code without launching a real subprocess.
/// </summary>
internal sealed class FixedSubprocessExitCodeSource : ISubprocessExitCodeSource
{
    private readonly int _code;
    public FixedSubprocessExitCodeSource(int code) => _code = code;
    public int GetExitCode() => _code;
}
