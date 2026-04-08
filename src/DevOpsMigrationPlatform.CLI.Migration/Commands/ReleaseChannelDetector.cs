using System.Reflection;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Detects the current release channel from the entry assembly's informational version,
/// which is stamped at build time by GitVersion via <c>/p:InformationalVersion=...</c>.
///
/// Mapping rules:
/// <list type="table">
///   <item><term>DEBUG build</term><description><see cref="ReleaseChannel.Local"/></description></item>
///   <item><term><c>1.0.0-preview.*</c></term><description><see cref="ReleaseChannel.Preview"/></description></item>
///   <item><term>any other pre-release label (alpha, canary, PullRequest, …)</term><description><see cref="ReleaseChannel.Canary"/></description></item>
///   <item><term><c>1.0.0</c> (no pre-release segment)</term><description><see cref="ReleaseChannel.Release"/></description></item>
/// </list>
/// </summary>
internal static class ReleaseChannelDetector
{
    /// <summary>The release channel for the currently running build.</summary>
    public static ReleaseChannel Current { get; } = Detect();

    private static ReleaseChannel Detect()
    {
#if DEBUG
        return ReleaseChannel.Local;
#else
        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? string.Empty;

        // Strip build metadata: "1.0.0-preview.1+Branch.main.Sha.abc" → "1.0.0-preview.1"
        var semver = version.Split('+')[0];

        // No hyphen → no pre-release label → production release
        if (!semver.Contains('-'))
            return ReleaseChannel.Release;

        // Grab the first pre-release identifier (e.g. "preview" from "preview.1")
        var label = semver[(semver.IndexOf('-') + 1)..].Split('.')[0].ToLowerInvariant();
        return label == "preview" ? ReleaseChannel.Preview : ReleaseChannel.Canary;
#endif
    }
}
