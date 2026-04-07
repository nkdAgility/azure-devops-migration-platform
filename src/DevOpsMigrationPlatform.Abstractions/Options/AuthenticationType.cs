namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Supported authentication mechanisms for source/target endpoints and organisation entries.
/// </summary>
public enum AuthenticationType
{
    /// <summary>Personal Access Token.</summary>
    Pat,

    /// <summary>Windows-integrated (NTLM/Kerberos) — TFS on-premises only.</summary>
    Windows
}
