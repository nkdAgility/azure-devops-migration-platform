namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Base extension options with only an Enabled flag.
/// </summary>
public class EnabledExtensionOptions
{
    /// <summary>Whether this extension is active. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;
}
