namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Represents the release stability tier of a CLI build, derived from the
/// assembly's <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/>
/// stamped at build time by GitVersion.
/// </summary>
internal enum ReleaseChannel
{
    /// <summary>DEBUG / local developer build — every command is visible.</summary>
    Local = 0,

    /// <summary>Feature-branch and PR builds (e.g. <c>1.0.0-alpha.3</c>).</summary>
    Canary = 1,

    /// <summary>Main-branch pre-release builds (e.g. <c>1.0.0-preview.1</c>).</summary>
    Preview = 2,

    /// <summary>Tagged production releases (e.g. <c>1.0.0</c>).</summary>
    Release = 3,
}
