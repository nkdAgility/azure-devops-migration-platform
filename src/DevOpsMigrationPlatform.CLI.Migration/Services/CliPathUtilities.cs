using SharedPathUtilities = DevOpsMigrationPlatform.Abstractions.Utilities.PathUtilities;

namespace DevOpsMigrationPlatform.CLI.Migration.Services;

/// <summary>
/// Thin wrapper that delegates to the shared <see cref="SharedPathUtilities"/>
/// in Abstractions. Kept so existing call-sites compile unchanged.
/// </summary>
internal static class CliPathUtilities
{
    public static string ExtractOrgFolderName(string url) =>
        SharedPathUtilities.ExtractOrgFolderName(url);

    public static string Sanitise(string name) =>
        SharedPathUtilities.Sanitise(name);
}
