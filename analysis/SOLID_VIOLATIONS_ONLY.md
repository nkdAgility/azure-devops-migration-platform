# Critical SOLID & Good Practice Violations

**Focus**: Architecture violations only. No feature work, no operational concerns.

---

## 🔴 Critical Violations (Architectural)

### 1. Job Engine Has UI Coupling (Console.WriteLine) — DIP & Testability Violation

**Principle Violated**: Dependency Inversion + Testability  
**Severity**: High  
**Files Affected**:
- `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Extensions/WorkItemStoreExtensions.cs` (lines 61, 113)
- `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/WorkItemExportService.cs`

**The Problem**:
```csharp
// ❌ VIOLATION: Job Engine writes to Console
catch (Exception ex)
{
    Console.WriteLine($"WIQL query failed: {ex.Message}");
    // ...
}
```

**Why It's a Violation**:
- **DIP**: Job Engine depends on concrete `Console` class, not abstraction
- **Testability**: Cannot suppress console output in tests
- **Guardrail Violation**: "No UI coupling in the Job Engine or modules"
- The engine is supposed to work in any context: local, Docker, cloud. Console breaks this.

**The Fix**:
```csharp
// ✅ CORRECT: Depend on IProgressSink abstraction
private readonly IProgressSink _progressSink;

catch (Exception ex)
{
    _progressSink.ReportProgress(new ProgressEvent(
        ModuleName: "WorkItems",
        Level: ProgressLevel.Warning,
        Message: $"WIQL query failed: {ex.Message}, reducing chunk size"));
}
```

**Implementation**:
- Add `IProgressSink` parameter to query methods
- Replace all `Console.WriteLine` with `_progressSink.ReportProgress`
- Propagate through call chain: extensions → services → module

**Test** (after fix):
```bash
# Should run without Console output leakage:
dotnet test --filter "TfsExport.*" 2>&1 | grep -i "wiql query failed"
# Should only appear in structured progress events, not Console
```

---

### 2. Optional Service Injection with Silent Skipping — SRP & Determinism Violation

**Principle Violated**: Single Responsibility + Determinism  
**Severity**: Medium  
**File Affected**: `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs`

**The Problem**:
```csharp
// ❌ VIOLATION: Module conditionally omits features with no logging
private readonly Infrastructure.Export.IWorkItemCommentSourceFactory? _commentSourceFactory;

public WorkItemsModule(
    IWorkItemRevisionSourceFactory sourceFactory,
    ILogger<WorkItemsModule> logger,
    Infrastructure.Export.IWorkItemCommentSourceFactory? commentSourceFactory = null)  // Optional!
{
    _commentSourceFactory = commentSourceFactory;
}

public async Task ExportAsync(ExportContext context, CancellationToken ct)
{
    // ... later ...
    IWorkItemCommentExportService? commentExportService = null;
    // If commentSourceFactory is null, comments are silently skipped
    // No warning logged. No checkpoint for "comments state".
}
```

**Why It's a Violation**:
- **SRP**: Module now has dual responsibility: export with comments OR without
- **Determinism**: Running twice with different dependency availability gives different results
- **No Observability**: User doesn't know comments were skipped
- **State Tracking**: If comments fail to import, no checkpoint prevents re-import of non-comments

**The Fix**:

Option A (Recommended): Require the dependency, always fail fast
```csharp
// ✅ CORRECT: No optional dependencies
public WorkItemsModule(
    IWorkItemRevisionSourceFactory sourceFactory,
    ILogger<WorkItemsModule> logger,
    IWorkItemCommentSourceFactory commentSourceFactory)  // Required
{
    _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _commentSourceFactory = commentSourceFactory ?? throw new ArgumentNullException(nameof(commentSourceFactory));
}

public async Task ExportAsync(ExportContext context, CancellationToken ct)
{
    // ... comments are ALWAYS available
}
```

Option B: Make it explicit and logged
```csharp
// ✅ Alternative: Explicit opt-in
public async Task ExportAsync(ExportContext context, CancellationToken ct)
{
    var includeComments = context.Job.Parameters?.Get<bool>("includeComments") ?? false;
    
    if (includeComments && _commentSourceFactory == null)
    {
        _logger.LogWarning("[WorkItems] Comments requested but source factory not available. Skipping comments.");
        context.ProgressSink.ReportProgress(new ProgressEvent(
            Level: ProgressLevel.Warning,
            Message: "Comments requested but unavailable"));
    }
    
    // Rest of export with explicit comment handling
}
```

**Why This Matters**:
- Idempotency: If you re-export, you know exactly what will happen
- Debuggability: Operator knows why comments missing (not just "they weren't included")
- Checkpoint safety: State is explicitly tracked, not implicitly skipped

---

### 3. Exception Handling is Too Generic — Error Handling Best Practice Violation

**Principle Violated**: Error Handling, Observability  
**Severity**: Medium  
**File Affected**: `src/DevOpsMigrationPlatform.CLI.Migration/Commands/CommandBase.cs`

**The Problem**:
```csharp
// ❌ VIOLATION: All exceptions treated identically
protected override async Task<int> ExecuteAsync(
    CommandContext context, TSettings settings, CancellationToken cancellationToken = default)
{
    try
    {
        return await ExecuteInternalAsync(context, settings, cancellationToken);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]✗[/] Unhandled exception: {Markup.Escape(ex.Message)}");
        return 1;  // Same exit code for all failures
    }
}
```

**Why It's a Violation**:
- **No distinction** between recoverable and unrecoverable errors
- **Same exit code** (1) for auth failure, network error, validation error, internal error
- **No guidance** — user doesn't know whether to retry, fix config, or report bug
- **Security risk**: Could expose credentials in ex.Message

**The Fix**:
```csharp
// ✅ CORRECT: Categorize exceptions and handle appropriately
catch (Exception ex)
{
    var category = CategorizeException(ex);
    var sanitized = ExceptionSanitizer.Sanitize(ex.Message);
    
    var (exitCode, guidance) = category switch
    {
        ExceptionCategory.Authentication =>
            (ExitCode.AuthenticationError, "Check PAT token validity and permissions (Code, Work Item Tracking)"),
        
        ExceptionCategory.NetworkError =>
            (ExitCode.TransientError, $"Network error: {sanitized} — check connection and retry"),
        
        ExceptionCategory.ValidationError =>
            (ExitCode.ValidationError, $"Configuration error: {sanitized} — see docs/configuration.md"),
        
        ExceptionCategory.PermissionDenied =>
            (ExitCode.PermissionError, "You lack permission to the specified project. Contact admin."),
        
        _ => (ExitCode.UnrecoverableError, $"Internal error: {sanitized} — please report this issue")
    };
    
    _logger.LogError(ex, "Job failed [{Category}]", category);
    AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(guidance)}[/]");
    return (int)exitCode;
}

private ExceptionCategory CategorizeException(Exception ex)
{
    return ex switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.InvalidCredentials => ExceptionCategory.Authentication,
        HttpStatusCode.Forbidden => ExceptionCategory.PermissionDenied,
        HttpRequestException httpEx when httpEx.InnerException is IOException 
            => ExceptionCategory.NetworkError,
        ValidationException => ExceptionCategory.ValidationError,
        _ => ExceptionCategory.Unknown
    };
}
```

**Why This Matters**:
- **Determinism**: User knows the exact failure reason
- **Recoverability**: Exit code tells scripts whether to retry
- **Observability**: Logs categorized by error type for analysis
- **Security**: Credentials masked from all outputs

---

## 🟡 Moderate Violations (Good Practices)

### 4. Unvalidated User Input (WIQL Injection Risk) — Input Validation Best Practice

**Principle Violated**: Security, Input Validation  
**Severity**: High (Security)  
**File Affected**: `src/DevOpsMigrationPlatform.CLI.Migration/Commands/MigrationExportCommand.cs` (approx.)

**The Problem**:
```csharp
// ❌ VIOLATION: User WIQL not validated
var query = ResolveParameter(job, "query", DefaultWiqlQuery);
// If user provides: "Select [System.Id] FROM WorkItems'; DROP TABLE [dbo.WorkItems]; --"
// This could become an injection attack on TFS/ADO
```

**Why It's a Violation**:
- **Input validation best practice**: Never trust user input
- **Injection risk**: WIQL could be abused to leak or modify data
- **No validation layer**: Config schema validation exists, but not WIQL structure

**The Fix**:
```csharp
// ✅ CORRECT: Validate WIQL before execution
public static class WiqlValidator
{
    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "UPDATE", "DELETE", "DROP", "CREATE", "ALTER", "TRUNCATE", "EXEC", "EXECUTE"
    };
    
    public static ValidationResult ValidateQuery(string wiql)
    {
        if (!wiql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return ValidationResult.Invalid("WIQL must start with SELECT");
        
        var tokens = wiql.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var token in tokens)
        {
            if (ForbiddenKeywords.Contains(token))
                return ValidationResult.Invalid($"WIQL cannot contain {token} operation");
        }
        
        // Could also call ADO API to validate syntax:
        // await _clientFactory.ValidateWiqlAsync(wiql);
        
        return ValidationResult.Valid();
    }
}

// Usage in command:
var validationResult = WiqlValidator.ValidateQuery(query);
if (!validationResult.IsValid)
    throw new ValidationException($"Invalid WIQL: {validationResult.Message}");
```

**Test**:
```csharp
[TestMethod]
public void Rejects_DELETE_Injection()
{
    var malicious = "SELECT [System.Id] FROM WorkItems'; DELETE FROM WorkItems WHERE '1'='1";
    var result = WiqlValidator.ValidateQuery(malicious);
    Assert.IsFalse(result.IsValid);
}
```

---

### 5. Credential Exposure in Exceptions — Security Best Practice

**Principle Violated**: Security, Error Handling  
**Severity**: High (Security)  
**File Affected**: `src/DevOpsMigrationPlatform.CLI.Migration/Commands/CommandBase.cs`

**The Problem**:
```csharp
// ❌ VIOLATION: Exception message could contain PAT
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]✗[/] Unhandled exception: {Markup.Escape(ex.Message)}");
    // If ex.Message = "401: Bearer pat_xyz123..." → credential exposed in logs/screenshots
}
```

**Why It's a Violation**:
- **Security best practice**: Never log credentials
- **Risk**: PAT tokens could be captured in logs, screenshots, or error reports
- **No masking**: Exception messages passed through unfiltered

**The Fix**:
```csharp
// ✅ CORRECT: Sanitize sensitive patterns
public static class ExceptionSanitizer
{
    private static readonly Regex[] SensitivePatterns = new[]
    {
        // PAT tokens: "pat_..." or "Bearer pat_..."
        new Regex(@"(pat_\w{34}|Bearer\s+pat_\w{34})", RegexOptions.IgnoreCase),
        
        // Basic auth: "user:password@"
        new Regex(@"(https?://)[^:]+:([^@]+)@", RegexOptions.IgnoreCase),
        
        // API keys in URLs: "?key=...&"
        new Regex(@"([?&])key=([^&]+)", RegexOptions.IgnoreCase),
        
        // Connection strings: "Password=..."
        new Regex(@"Password\s*=\s*[^;]+", RegexOptions.IgnoreCase),
    };
    
    public static string Sanitize(string text)
    {
        var sanitized = text;
        foreach (var pattern in SensitivePatterns)
        {
            sanitized = pattern.Replace(sanitized, "$1[REDACTED]");
        }
        return sanitized;
    }
}

// Usage:
catch (Exception ex)
{
    var sanitized = ExceptionSanitizer.Sanitize(ex.Message);
    AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(sanitized)}");
    _logger.LogError(ex, "Exception (message sanitized)");
}
```

**Test**:
```csharp
[TestMethod]
public void Masks_PAT_Tokens()
{
    var exposed = "401: Bearer pat_xyzabc123def456ghi";
    var sanitized = ExceptionSanitizer.Sanitize(exposed);
    Assert.AreEqual("401: [REDACTED]", sanitized);
}

[TestMethod]
public void Masks_Basic_Auth()
{
    var exposed = "https://admin:SecureP@ss123@dev.azure.com/tfs";
    var sanitized = ExceptionSanitizer.Sanitize(exposed);
    Assert.IsFalse(sanitized.Contains("SecureP@ss123"));
}
```

---

### 6. No Concurrent Write Detection — Data Integrity Best Practice

**Principle Violated**: Data Integrity  
**Severity**: Medium  
**Scope**: Architecture-level (IArtefactStore)

**The Problem**:
If two agents write to the same package simultaneously:
```csharp
// ❌ VIOLATION: No conflict detection
await artefactStore.WriteAsync("WorkItems/2024-01-01/00000001-1-0/revision.json", content);
// If another agent writes to same path concurrently, race condition possible
```

**Why It's a Violation**:
- **Data Integrity**: Concurrent writes could corrupt package
- **No guarding**: Lease prevents this in practice, but not enforced at store level
- **Silent failure**: If both agents write, last-write-wins (data loss possible)

**The Guardrail States**: "Agents are stateless; all durable state is in the package"  
**But missing**: Atomic write enforcement

**The Fix**:
Add lease verification to `IArtefactStore.WriteAsync`:

```csharp
// ✅ CORRECT: Verify write authorization
public interface IArtefactStore
{
    // Existing methods...
    
    /// <summary>
    /// Writes with lease token to ensure only lease holder can write.
    /// Throws LeaseExpiredException if token invalid or expired.
    /// </summary>
    Task WriteAsync(string path, string content, string leaseToken, CancellationToken cancellationToken);
}

// Usage in Agent:
public async Task ExportAsync(MigrationJob job, string leaseToken, CancellationToken ct)
{
    // Each write includes lease token
    await artefactStore.WriteAsync(
        "WorkItems/2024-01-01/00000001-1-0/revision.json",
        content,
        leaseToken,  // ← Proves this agent holds the lease
        ct);
}

// Implementation:
public async Task WriteAsync(string path, string content, string leaseToken, CancellationToken ct)
{
    // Verify lease is still valid
    if (!await _leaseService.IsValidAsync(leaseToken, ct))
        throw new LeaseExpiredException("Write not authorized: lease expired");
    
    // Proceed with write
    await WriteAsync(path, content, ct);
}
```

**Alternatively (simpler)**: Document the lease as a protocol requirement, not code enforcement.

---

### 7. Missing Exception Handling in Critical Paths — Error Handling Best Practice

**Principle Violated**: Error Handling  
**Severity**: Medium  
**Files Affected**: Multiple (if applicable)

**The Problem**:
Some catch blocks swallow errors without logging context:

```csharp
// ❌ Potential concern: Generic catch without context
catch (Exception ex)
{
    _logger.LogError(ex, "Error");  // No context about what operation
    // Which item? Which module? Which stage?
}
```

**Why It's a Violation**:
- **Lack of observability**: Cannot correlate error to specific item/job
- **Non-deterministic debugging**: Same error twice, same generic message

**The Fix**:
```csharp
// ✅ CORRECT: Rich error context
catch (Exception ex)
{
    _logger.LogError(ex,
        "Failed to export revision {RevisionPath} in job {JobId} at stage {Stage}",
        revisionPath,
        job.Id,
        "AttachmentDownload");
    
    context.ProgressSink.ReportProgress(new ProgressEvent(
        Level: ProgressLevel.Error,
        Message: $"Failed to download attachment for {revisionPath}: {ex.Message}",
        JobId: job.Id,
        Stage: "Attachments"));
}
```

---

## 📊 Summary: SOLID/Good Practice Violations Only

| # | Violation | Principle | Severity | Fix Effort |
|---|-----------|-----------|----------|-----------|
| 1 | Console.WriteLine in Job Engine | DIP + Testability | 🔴 High | 2-3 hours |
| 2 | Optional service injection (comments) | SRP + Determinism | 🟡 Medium | 2-3 hours |
| 3 | Generic exception handling | Error Handling | 🟡 Medium | 3-4 hours |
| 4 | Unvalidated user WIQL | Input Validation | 🔴 High | 1-2 hours |
| 5 | Credentials in exceptions | Security Best Practice | 🔴 High | 1-2 hours |
| 6 | No concurrent write detection | Data Integrity | 🟡 Medium | 2-4 hours |

---

## 🎯 Recommended Fix Order

**Week 1 (Critical—8-10 hours)**:
1. ✅ Remove Console.WriteLine (2-3h)
2. ✅ Validate WIQL input (1-2h)
3. ✅ Mask credentials in exceptions (1-2h)

**Week 2 (Important—6-8 hours)**:
4. ✅ Categorize exceptions properly (3-4h)
5. ✅ Fix optional service injection (2-3h)

**Week 3+ (Nice-to-have)**:
6. ✅ Enforce concurrent write detection (2-4h)

All of these affect **SOLID compliance** and **good practices only**—not features, not operations.

