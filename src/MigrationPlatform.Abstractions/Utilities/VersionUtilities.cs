using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MigrationPlatform.Abstractions.Utilities
{
    public static class VersionUtilities
    {
        public static (Version version, string PreReleaseLabel, string versionString) GetRunningVersion()
        {
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly == null)
            {
                throw new InvalidOperationException("Entry assembly is null. Unable to retrieve version information.");
            }

            var location = entryAssembly.Location;
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new InvalidOperationException("Assembly location is null or empty. Cannot retrieve version info.");
            }

            var fileVersionInfo = FileVersionInfo.GetVersionInfo(location);
            if (fileVersionInfo == null)
            {
                throw new InvalidOperationException("Failed to retrieve FileVersionInfo from assembly.");
            }

            var productVersion = fileVersionInfo.ProductVersion;
            if (string.IsNullOrWhiteSpace(productVersion))
            {
                throw new InvalidOperationException("Product version not found in FileVersionInfo.");
            }

            var matches = Regex.Matches(productVersion,
                @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<build>0|[1-9]\d*)(?:-(?<label>(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+(?<fullEnd>[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$");

            if (matches.Count == 0)
            {
                throw new FormatException($"ProductVersion '{productVersion}' does not match expected version format.");
            }

            var versionLabel = matches[0].Groups["label"].Value;

            var fileVersion = fileVersionInfo.FileVersion;
            if (string.IsNullOrWhiteSpace(fileVersion))
            {
                throw new InvalidOperationException("FileVersion is null or empty.");
            }

            Version version;
            try
            {
                version = new Version(fileVersion);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to parse version from FileVersion.", ex);
            }

            var versionString = $"v{version.Major}.{version.Minor}.{version.Build}" +
                                (string.IsNullOrEmpty(versionLabel) ? "" : $"-{versionLabel}");

            return (version, versionLabel, versionString);
        }
    }
}
