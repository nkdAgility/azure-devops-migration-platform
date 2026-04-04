# Contract: `ITokenResolver`

**Project**: `DevOpsMigrationPlatform.Abstractions`  
**Namespace**: `DevOpsMigrationPlatform.Abstractions`  
**Implementation**: `DevOpsMigrationPlatform.Infrastructure.Config.TokenResolver`  
**Registered by**: `InventoryServiceExtensions.AddInventoryServices`

---

## Interface Definition

```csharp
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Resolves a raw token string, expanding "$ENV:VARNAME" references to their
/// current environment variable values.
///
/// Contract:
/// - A plain string (no "$ENV:" prefix) is returned unchanged.
/// - A "$ENV:VARNAME" string returns the value of the named environment variable.
/// - A "$ENV:" string with no variable name throws <see cref="InvalidOperationException"/>.
/// - A "$ENV:VARNAME" string where the variable is not set throws <see cref="InvalidOperationException"/>.
///
/// Implementations MUST NOT log the resolved token value.
/// </summary>
public interface ITokenResolver
{
    /// <summary>
    /// Resolves <paramref name="rawToken"/> and returns the effective token value.
    /// </summary>
    /// <param name="rawToken">
    /// A plain PAT string, or a "$ENV:VARNAME" reference string.
    /// Must not be null or empty — callers should validate the config before calling.
    /// </param>
    /// <returns>The resolved token value.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the "$ENV:" prefix is present but the variable name is missing,
    /// or when the referenced environment variable is not set.
    /// </exception>
    string Resolve(string rawToken);
}
```

---

## Behaviour Specification

| Input | Output | Side Effects |
|---|---|---|
| `"mysecretpat123"` | `"mysecretpat123"` | None |
| `"$ENV:ADO_PAT"` (ADO_PAT=abc123) | `"abc123"` | None — value never written to any sink |
| `"$ENV:"` | — | Throws `InvalidOperationException`: `"Malformed token reference '$ENV:' — variable name is missing."` |
| `"$ENV:MISSING"` (not set) | — | Throws `InvalidOperationException`: `"Environment variable 'MISSING' referenced in config token is not set."` |
| `"$ENV:ADO_PAT"` (ADO_PAT=`""`) | — | Throws `InvalidOperationException`: `"Environment variable 'ADO_PAT' is set but empty."` |

**Security note**: Resolved token values must never be written to log output, `IProgressSink`, or any diagnostic sink. `InventoryCommand` must store the resolved value in a local variable and not pass it to any logging call.

---

## Concrete Implementation Sketch

```csharp
// DevOpsMigrationPlatform.Infrastructure/Config/TokenResolver.cs
namespace DevOpsMigrationPlatform.Infrastructure;

internal sealed class TokenResolver : ITokenResolver
{
    private const string EnvPrefix = "$ENV:";

    public string Resolve(string rawToken)
    {
        if (!rawToken.StartsWith(EnvPrefix, StringComparison.Ordinal))
            return rawToken;

        var varName = rawToken[EnvPrefix.Length..];

        if (string.IsNullOrEmpty(varName))
            throw new InvalidOperationException(
                "Malformed token reference '$ENV:' — variable name is missing.");

        var value = Environment.GetEnvironmentVariable(varName);

        if (value is null)
            throw new InvalidOperationException(
                $"Environment variable '{varName}' referenced in config token is not set.");

        if (value.Length == 0)
            throw new InvalidOperationException(
                $"Environment variable '{varName}' is set but empty.");

        return value;
    }
}
```

---

## Registration

```csharp
// In InventoryServiceExtensions.AddInventoryServices:
services.AddSingleton<ITokenResolver, TokenResolver>();
```

---

## Tests (acceptance criteria mapping)

| User Story | Scenario | Test |
|---|---|---|
| US-2 | Token `$ENV:ADO_PAT` with variable set → succeeds | `Resolve_EnvPrefix_VariableSet_ReturnsValue` |
| US-2 | Token `$ENV:MISSING_VAR` with variable unset → throws with variable name | `Resolve_EnvPrefix_MissingVariable_ThrowsWithVariableName` |
| US-2 | Plain PAT (no `$ENV:`) → returned unchanged | `Resolve_PlainToken_ReturnsUnchanged` |
| US-2 | `$ENV:` with no variable name → throws with clear message | `Resolve_MalformedPrefix_ThrowsMalformedMessage` |
