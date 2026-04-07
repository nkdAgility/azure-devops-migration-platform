namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Supported authentication mechanisms for source/target endpoints and organisation entries.
/// </summary>
public enum AuthenticationType
{
    /// <summary>
    /// No authentication block was specified in the configuration.
    /// No PAT resolution is performed; <c>accessToken</c> is ignored.
    /// Used for Windows-integrated auth entries that omit the <c>authentication</c> block entirely.
    /// </summary>
    None = 0,

    /// <summary>Personal Access Token.</summary>
    Pat,

    /// <summary>Windows-integrated (NTLM/Kerberos) — TFS on-premises only.</summary>
    Windows
}
