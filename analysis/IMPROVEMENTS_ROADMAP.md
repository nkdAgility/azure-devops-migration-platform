# Improvement Plan: Azure DevOps Migration Platform

**Baseline Score**: 8.8/10  
**Target Score**: 9.5+/10  
**Effort Estimate**: 20-25 days of development

---

## 1. Critical Issues (Must Fix)

### 1.1 Console.WriteLine in TFS Export Path ⛔

**Severity**: Medium  
**Score Impact**: Security & Logging (8.0/10 → 7.5/10)  
**Effort**: 2-3 hours

**Issue**: 
The TFS export path uses `Console.WriteLine` instead of `IProgressSink`:
- [WorkItemStoreExtensions.cs](src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Extensions/WorkItemStoreExtensions.cs#L61)
- [WorkItemStoreExtensions.cs](src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Extensions/WorkItemStoreExtensions.cs#L113)

```csharp
// ❌ Current:
catch (Exception ex)
{
    Console.WriteLine($"WIQL query failed: {ex.Message}");
    chunkSize = TimeSpan.FromTicks(chunkSize.Ticks / 2);
    continue;
}
```

**Why It Matters**:
- Progress is lost if stdout is redirected
- Cannot correlate with structured logging
- Violates contract that Job Engine has "no UI coupling"

**Fix**:
```csharp
// ✅ Solution: Inject IProgressSink
private readonly IProgressSink _progressSink;

// In catch blocks:
_progressSink.ReportProgress(new ProgressEvent(
    ModuleName: "WorkItems",
    Current: queryIndex,
    Level: ProgressLevel.Warning,
    Message: $"WIQL query failed: {ex.Message}, reducing chunk size to {chunkSize}"
));
```

**Implementation Steps**:
1. Add `IProgressSink` parameter to `QueryAllByChangedDate` and `QueryAllByDateChunk`
2. Replace `Console.WriteLine` calls with `_progressSink.ReportProgress`
3. Update callers to pass progress sink

**Files to Modify**:
- `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Extensions/WorkItemStoreExtensions.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/WorkItemExportService.cs` (callers)

**Testing**:
```bash
# TfsExport system test should verify no Console.WriteLine:
dotnet test --filter "TfsExport.*SystemTest"
```

---

### 1.2 Implement Deferred WorkItems Import ⛔

**Severity**: High  
**Score Impact**: LSP (8.8/10 → 8.5/10)  
**Effort**: 3-5 days

**Issue**:
`WorkItemsModule.ImportAsync` throws `NotSupportedException`:

```csharp
// ❌ Current:
public Task ImportAsync(ImportContext context, CancellationToken ct) =>
    throw new NotSupportedException("WorkItems import is not yet supported.");
```

**Why It Matters**:
- Violates Liskov Substitution Principle
- Prevents "Both" mode (export + import in one run)
- Blocks system integration tests

**Fix**:
Based on the specs, `IWorkItemImportSink` abstraction should be used:

```csharp
// ✅ Solution:
private readonly IWorkItemImportSink _importSink;

public async Task ImportAsync(ImportContext context, CancellationToken ct)
{
    var revisions = context.ArtefactStore.EnumerateAsync("WorkItems", ct);
    var checkpointingService = new CheckpointingService(context.StateStore);
    
    await foreach (var revisionPath in revisions)
    {
        var revision = await context.ArtefactStore.ReadAsync(
            $"{revisionPath}/revision.json", ct);
        var typedRevision = JsonSerializer.Deserialize<WorkItemRevision>(revision);
        
        await _importSink.WriteRevisionAsync(typedRevision, context.ArtefactStore, ct);
        await checkpointingService.UpdateCursorAsync(revisionPath, ct);
    }
}
```

**Implementation Steps**:
1. Ensure `IWorkItemImportSink` exists in Abstractions (verify in specs)
2. Create `AzureDevOpsWorkItemImportSink` in `Infrastructure.AzureDevOps`
3. Implement `WorkItemsModule.ImportAsync` using sink
4. Add unit tests for import path
5. Add integration test for full export→import cycle

**Files to Create/Modify**:
- `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemImportSink.cs` (if missing)
- `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/AzureDevOpsWorkItemImportSink.cs` (new)
- `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Modules/WorkItemsModuleImportTests.cs`

**Testing**:
```bash
# System test for both mode:
dotnet test --filter "SystemTest.*Both.*Mode"
```

---

### 1.3 WIQL Injection Vulnerability 🔓

**Severity**: Critical  
**Score Impact**: Security (8.0/10 → 7.0/10)  
**Effort**: 1-2 days

**Issue**:
User-supplied WIQL queries are not validated:

```csharp
// ❌ Current:
var query = ResolveParameter(job, "query", DefaultWiqlQuery);
// No validation — user could inject malicious WIQL
```

**Why It Matters**:
- Allows query injection attacks
- Could leak sensitive work item data
- Azure DevOps org vulnerability

**Fix**:
```csharp
// ✅ Solution: Validate WIQL structure
public static class WiqlValidator
{
    private static readonly HashSet<string> ForbiddenKeywords = new()
    {
        "RECYCLE", "DELETE", "UPDATE", "DROP", "CREATE", "ALTER"
    };
    
    public static ValidationResult ValidateQuery(string wiql)
    {
        var upperWiql = wiql.ToUpperInvariant();
        
        // Only SELECT allowed
        if (!upperWiql.TrimStart().StartsWith("SELECT"))
            return ValidationResult.Invalid("WIQL must start with SELECT");
        
        // Reject dangerous operations
        foreach (var keyword in ForbiddenKeywords)
        {
            if (upperWiql.Contains(keyword))
                return ValidationResult.Invalid($"WIQL cannot contain {keyword}");
        }
        
        // Validate syntax with ADO API
        try
        {
            // Call ADO API with dry-run flag (if available)
            // or parse with lightweight validator
        }
        catch (Exception ex)
        {
            return ValidationResult.Invalid($"WIQL syntax error: {ex.Message}");
        }
        
        return ValidationResult.Valid();
    }
}
```

**Implementation Steps**:
1. Create `WiqlValidator` in `Infrastructure.AzureDevOps`
2. Call validator in `MigrationExportCommand` before submission
3. Add comprehensive test cases for common injection vectors
4. Document in config schema that user WIQL is validated

**Files to Create/Modify**:
- `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/WiqlValidator.cs` (new)
- `src/DevOpsMigrationPlatform.CLI.Migration/Commands/MigrationExportCommand.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Tests/WiqlValidatorTests.cs` (new)

**Testing**:
```csharp
// Test cases:
[TestMethod] public void Rejects_DELETE_Query() 
[TestMethod] public void Rejects_UPDATE_Query()
[TestMethod] public void Accepts_Valid_SELECT_Query()
[TestMethod] public void Rejects_Non_SELECT_Query()
```

---

### 1.4 Credential Masking in Exceptions 🔓

**Severity**: High  
**Score Impact**: Security (8.0/10 → 7.5/10)  
**Effort**: 1-2 days

**Issue**:
Exception messages could expose Azure DevOps PAT tokens:

```csharp
// ❌ Current:
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]✗[/] Unhandled exception: {Markup.Escape(ex.Message)}");
    // If ex contains "401 Unauthorized: {pat}", it's logged
}
```

**Why It Matters**:
- PAT tokens could be captured in logs
- Screenshots/recordings expose credentials
- Violates security best practices

**Fix**:
```csharp
// ✅ Solution: Sanitize exceptions
public static class ExceptionSanitizer
{
    private static readonly Regex PatPattern = new(@"pat|token|credential|password|secret|auth", 
        RegexOptions.IgnoreCase);
    
    public static string Sanitize(Exception ex)
    {
        var message = ex.Message;
        
        // Mask PAT patterns
        message = Regex.Replace(message, PatPattern, "[REDACTED]");
        
        // Mask URLs with embedded credentials
        message = Regex.Replace(message, 
            @"https://([^:]+):([^@]+)@", 
            "https://[user]:[REDACTED]@");
        
        return message;
    }
}

// Usage:
catch (Exception ex)
{
    var sanitized = ExceptionSanitizer.Sanitize(ex);
    AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(sanitized)}");
    _logger.LogError(ex, "Unhandled exception (see console for details)");
}
```

**Implementation Steps**:
1. Create `ExceptionSanitizer` utility
2. Update `CommandBase.ExecuteAsync` to use sanitizer
3. Update all exception handlers to sanitize before logging
4. Add unit tests for sanitization patterns
5. Add integration test capturing exception output

**Files to Create/Modify**:
- `src/DevOpsMigrationPlatform.CLI.Migration/Utilities/ExceptionSanitizer.cs` (new)
- `src/DevOpsMigrationPlatform.CLI.Migration/Commands/CommandBase.cs`
- `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Utilities/ExceptionSanitizerTests.cs`

**Testing**:
```csharp
[TestMethod] 
public void Masks_PAT_Tokens() =>
    Assert.That(Sanitize("Bearer pat_xyz123abc"), Contains, "[REDACTED]");
    
[TestMethod]
public void Masks_URL_Credentials() =>
    Assert.That(Sanitize("https://user:pat@dev.azure.com"), Contains, "[REDACTED]");
```

---

## 2. High-Value Improvements (Quick Wins)

### 2.1 Add Health Check Endpoints 🏥

**Severity**: Medium  
**Score Impact**: Deployment (8.1/10 → 8.7/10)  
**Effort**: 2-3 days

**Why It Matters**:
- Container Apps requires `/health` endpoint for readiness probes
- Current no way to verify ControlPlane is ready
- Enables automated deployment validation

**Solutions**:

**Location**: `ControlPlaneHost` (ASP.NET Core)

```csharp
// Add to Program.cs:
builder.Services.AddHealthChecks()
    .AddCheck("database", new DatabaseHealthCheck(dbContext))
    .AddCheck("storage", new StorageHealthCheck(artefactStore));

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true,  // Include all checks
});
```

**Endpoint Contracts**:
- `GET /health/live` → 200 if service is running (no dependencies)
- `GET /health/ready` → 200 if all dependencies are available

**Implementation Steps**:
1. Add `Microsoft.AspNetCore.Diagnostics.HealthChecks` NuGet
2. Create `DatabaseHealthCheck` → verify EF Core connection
3. Create `StorageHealthCheck` → verify artefact store connectivity
4. Register in DI
5. Add to `ServiceDefaults` for Aspire

**Files to Create/Modify**:
- `src/DevOpsMigrationPlatform.ControlPlaneHost/Program.cs`
- `src/DevOpsMigrationPlatform.ControlPlaneHost/Health/DatabaseHealthCheck.cs` (new)
- `src/DevOpsMigrationPlatform.ControlPlaneHost/Health/StorageHealthCheck.cs` (new)
- `src/DevOpsMigrationPlatform.ServiceDefaults/Extensions.cs` (register)

**Testing**:
```bash
# After starting ControlPlaneHost:
curl http://localhost:5100/health/live
# Should return 200 with {"status":"Healthy"}

curl http://localhost:5100/health/ready
# Should return 200 or 503 if dependencies missing
```

---

### 2.2 Structured Logging with OpenTelemetry 📊

**Severity**: Medium  
**Score Impact**: Observability (7.8/10 → 8.5/10)  
**Effort**: 3-4 days

**Why It Matters**:
- Distributed tracing across CLI → ControlPlane → Agent
- Correlation IDs for end-to-end tracking
- Structured logs for search and analysis

**Solution**:

Add to all key paths:

```csharp
// CLi.Migration/Commands/MigrationExportCommand.cs:
using System.Diagnostics;

var activity = new Activity("ExportJob").Start();
using (activity)
{
    activity.SetTag("job.id", job.Id);
    activity.SetTag("source.url", job.Source.ResolvedUrl);
    
    _logger.LogInformation(
        "Export job {JobId} started for {SourceUrl}",
        job.Id, 
        job.Source.ResolvedUrl);
    
    try
    {
        await _controlPlaneClient.SubmitJobAsync(job, ct);
    }
    finally
    {
        activity.SetStatus(ActivityStatusCode.Ok);  // or Error
    }
}
```

**Implementation Steps**:
1. Add `System.Diagnostics.DiagnosticSource` NuGet references
2. Create trace scopes around major operations
3. Add correlation IDs to all inter-service calls
4. Configure OpenTelemetry exporter (already in ServiceDefaults?)
5. Add integration test that captures and validates traces

**Files to Create/Modify**:
- `src/DevOpsMigrationPlatform.ServiceDefaults/Extensions.cs` (OpenTelemetry config)
- `src/DevOpsMigrationPlatform.CLI.Migration/Commands/MigrationExportCommand.cs`
- `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentWorker.cs`
- `src/DevOpsMigrationPlatform.ControlPlane/Controllers/JobsController.cs`

**Testing**:
```bash
# System test with trace collection:
dotnet test --filter "SystemTest.*Tracing"
# Verify traces appear in configured exporter
```

---

### 2.3 Create Operational Runbooks 📖

**Severity**: Medium  
**Score Impact**: Documentation (7.9/10 → 8.5/10)  
**Effort**: 1-2 days

**Why It Matters**:
- Operations team needs how-to guides
- Reduces mean-time-to-resolution for issues
- Enables runbook automation

**Documents to Create**:

**File**: `docs/operations/runbooks.md`

```markdown
# Operational Runbooks

## How to Resume a Stuck Migration

### Symptom
- Job status shows "In Progress" but no activity for 5+ minutes
- Migration Agent logs show no recent entries

### Resolution
1. **Classify the failure**:
   ```bash
   # Check agent logs
   kubectl logs -n devops-migration agent-pod-name
   
   # Check control plane logs
   kubectl logs -n devops-migration controlplane-pod-name
   ```

2. **If Agent crashed**:
   - Control Plane automatically replaces crashed agent
   - Wait 2-3 minutes for new agent to pick up job
   - If not progressing after 5 min, check Control Plane logs

3. **If stuck in specific module**:
   - Check `Checkpoints/ModuleName/cursor.json` in package
   - If cursor is stale (> 5 min old), restart agent
   - If cursor is recent, check storage logs for write errors

4. **Force restart**:
   ```bash
   # 1. Get job ID from control plane
   curl https://controlplane.example.com/api/jobs | jq '.[] | select(.status=="InProgress")'
   
   # 2. Delete agent lease to trigger restart
   psql postgresql://... -c "DELETE FROM agent_leases WHERE job_id='xyz'"
   
   # 3. Trigger new agent pickup
   # (automatic, should happen within 30 seconds)
   ```

## How to Monitor Job Progress

### Real-time Monitoring
```bash
# In TUI
devopsmigration tui --api-url https://controlplane.example.com

# Or API polling
curl https://controlplane.example.com/api/jobs/xyz | jq '.progress'
```

## How to Diagnose Connection Issues

### PAT Token Invalid
**Symptom**: "401 Unauthorized"
```bash
# Verify PAT has correct scopes:
# - Code (Read)
# - Work Item Tracking (Read & Write)

# Test connection:
curl -u ":<PAT>" https://dev.azure.com/_apis/projects
```

## How to Handle Large Migration Failures

### If export fails
- Checkpoint cursor prevents re-exporting items
- Fix the issue (connectivity, quota, etc.)
- Re-run export — will resume from cursor

### If import fails
- All imported items are transactional per module
- Check `Logs/ModuleName/` for which items failed
- Fix underlying issue (schema, duplicate ID, etc.)
- Re-run import — will skip already-imported items (needs idempotency key)

```

**Files to Create**:
- `docs/operations/runbooks.md` (new)
- `docs/operations/monitoring.md` (new)
- `docs/operations/troubleshooting.md` (new)
- `docs/operations/disaster-recovery.md` (new)

---

### 2.4 CLI SQL Injection Test 🧪

**Severity**: Low  
**Score Impact**: Security (8.0/10 → 8.2/10)  
**Effort**: 1 day

**Why It Matters**:
- ControlPlane uses EF Core (parameterized by default, but worth verifying)
- Need to ensure no raw SQL in queries

**Solution**:

Create security test:

```csharp
// tests/DevOpsMigrationPlatform.ControlPlane.Tests/Security/SqlInjectionTests.cs

[TestClass]
public class SqlInjectionTests
{
    private readonly ControlPlaneDbContext _dbContext;
    
    [TestInitialize]
    public void Setup()
    {
        // Create test DB
        _dbContext = new ControlPlaneDbContext(
            new DbContextOptionsBuilder()
                .UseSqlite("Data Source=:memory:")
                .Options);
        _dbContext.Database.EnsureCreated();
    }
    
    [TestMethod]
    public async Task Job_Search_Resists_SQL_Injection()
    {
        var maliciousId = "'; DROP TABLE jobs; --";
        
        var result = await _dbContext.Jobs
            .Where(j => j.Id == maliciousId)
            .ToListAsync();
        
        // Should return empty, not throw or drop table
        Assert.AreEqual(0, result.Count);
        
        // Verify table still exists
        Assert.IsTrue(await _dbContext.Jobs.AnyAsync());
    }
    
    [TestMethod]
    public async Task Job_Search_By_SourceUrl_Resistant()
    {
        var maliciousUrl = "http://example.com' OR '1'='1";
        
        var result = await _dbContext.Jobs
            .Where(j => j.SourceUrl == maliciousUrl)
            .ToListAsync();
        
        Assert.AreEqual(0, result.Count);
    }
}
```

**Files to Create**:
- `tests/DevOpsMigrationPlatform.ControlPlane.Tests/Security/SqlInjectionTests.cs`

---

## 3. Medium-Term Improvements (1-2 weeks)

### 3.1 Comprehensive Integration Test Suite 🧪

**Score Impact**: Testability (8.5/10 → 9.2/10)  
**Effort**: 5-7 days

**Current Gap**: No visible E2E tests using real PostgreSQL and storage

**Solution**:

```csharp
// tests/DevOpsMigrationPlatform.IntegrationTests/E2E/ExportImportE2ETest.cs

[TestClass]
[TestCategory("IntegrationTest")]
public class ExportImportE2ETest
{
    private PostgreSqlContainer _postgresContainer;
    private MockAzureDevOpsServer _mockAdoServer;
    private AppHost _appHost;
    
    [TestInitialize]
    public async Task Setup()
    {
        // Start PostgreSQL container
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .Build();
        await _postgresContainer.StartAsync();
        
        // Start mocked Azure DevOps (or use Testcontainers)
        _mockAdoServer = new MockAzureDevOpsServer();
        _mockAdoServer.AddProject("test-project");
        _mockAdoServer.AddWorkItems(10);
        await _mockAdoServer.StartAsync();
        
        // Start AppHost with overrides
        _appHost = new AppHost();
        _appHost.Override("database", _postgresContainer.GetConnectionString());
        _appHost.Override("ado-url", _mockAdoServer.Url);
    }
    
    [TestMethod]
    public async Task Export_Export_Import_Preserves_Data()
    {
        // 1. Export from mock ADO
        var exportConfig = new MigrationOptions
        {
            Mode = MigrationMode.Export,
            Source = new SourceOptions { Url = _mockAdoServer.Url, Project = "test-project" },
            Package = new PackageOptions { Url = "file:///tmp/migration-package" }
        };
        
        var cli = new DevOpsMigrationCli(_appHost.GetControlPlaneUrl());
        var exportJob = await cli.ExportAsync(exportConfig);
        await WaitForJobCompletion(exportJob.Id, timeout: TimeSpan.FromMinutes(5));
        
        // 2. Import to target (mock)
        var importConfig = new MigrationOptions
        {
            Mode = MigrationMode.Import,
            Package = new PackageOptions { Url = "file:///tmp/migration-package" },
            Target = new TargetOptions { Url = _mockAdoServer.Url, Project = "test-project" }
        };
        
        var importJob = await cli.ImportAsync(importConfig);
        await WaitForJobCompletion(importJob.Id, timeout: TimeSpan.FromMinutes(5));
        
        // 3. Verify data matches
        var exportedItems = await _mockAdoServer.GetWorkItemsAsync("test-project");
        Assert.AreEqual(10, exportedItems.Count);
    }
}
```

**Testcontainers Setup**:
```xml
<!-- .csproj -->
<ItemGroup>
  <PackageReference Include="Testcontainers" Version="3.10.0" />
  <PackageReference Include="Testcontainers.PostgreSql" Version="3.10.0" />
</ItemGroup>
```

**Files to Create**:
- `tests/DevOpsMigrationPlatform.IntegrationTests/` (directory)
- `tests/DevOpsMigrationPlatform.IntegrationTests/E2E/ExportImportE2ETest.cs`
- `tests/DevOpsMigrationPlatform.IntegrationTests/Fixtures/ContainerFixture.cs`

---

### 3.2 Exception Categorization Framework 🛡️

**Score Impact**: Error Handling (8.3/10 → 8.8/10)  
**Effort**: 2-3 days

**Why It Matters**:
- Current error handling is generic catch-all
- Need specific handling for: auth, network, validation, data errors
- Enables better retry strategies

**Solution**:

```csharp
// src/DevOpsMigrationPlatform.Abstractions/Errors/MigrationException.cs

public abstract class MigrationException : Exception
{
    public abstract MigrationErrorCategory Category { get; }
    public bool IsRetryable { get; protected set; }
    
    protected MigrationException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}

public enum MigrationErrorCategory
{
    Authentication,      // 401 — expired PAT, wrong PAT
    Authorization,       // 403 — no permission to project
    ResourceNotFound,    // 404 — project doesn't exist
    Conflict,            // 409 — item already imported
    RateLimited,         // 429 — quota exceeded
    InternalServer,      // 500+ — remote system error
    NetworkError,        // IOException, HttpRequestException
    ValidationError,     // Config schema, WIQL syntax
    DataIntegrity,       // Missing required field, corrupt data
    Unknown              // Everything else
}

// Specific exceptions:
public class AuthenticationException : MigrationException
{
    public override MigrationErrorCategory Category => MigrationErrorCategory.Authentication;
    public AuthenticationException(string message) : base(message) { IsRetryable = false; }
}

public class RateLimitedException : MigrationException
{
    public override MigrationErrorCategory Category => MigrationErrorCategory.RateLimited;
    public TimeSpan RetryAfter { get; set; }
    public RateLimitedException(string message, TimeSpan retryAfter) : base(message)
    {
        IsRetryable = true;
        RetryAfter = retryAfter;
    }
}

// Usage in operators:
catch (HttpStatusCode statusCode) // or HttpRequestException
{
    throw statusCode switch
    {
        401 => new AuthenticationException("PAT token is invalid or expired"),
        403 => new AuthorizationException("No access to specified project"),
        404 => new ResourceNotFoundException("Project or organization not found"),
        429 => new RateLimitedException("Quota exceeded", TimeSpan.FromMinutes(1)),
        _ => new MigrationException($"Remote service error: {statusCode}")
    };
}
```

**Error Reporting**:
```csharp
// In CommandBase.ExecuteAsync:
catch (MigrationException ex)
{
    _logger.LogError(ex, "Migration failed [{Category}]: {Message}", ex.Category, ex.Message);
    
    // Provide user guidance based on category
    var guidance = GetGuidanceFor(ex.Category);
    AnsiConsole.MarkupLine($"[yellow]{guidance}[/]");
    
    return ex.Category switch
    {
        Authentication => ExitCode.AuthenticationError,
        RateLimited => ExitCode.TransientError,  // Encourages retry
        _ => ExitCode.UnrecoverableError
    };
}
```

**Files to Create/Modify**:
- `src/DevOpsMigrationPlatform.Abstractions/Errors/MigrationException.cs` (new)
- `src/DevOpsMigrationPlatform.Abstractions/Errors/MigrationErrorCategory.cs` (new)
- `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Errors/` (specific exception types)
- `src/DevOpsMigrationPlatform.CLI.Migration/Commands/CommandBase.cs` (update error handling)

---

## 4. Long-Term Improvements (2-4 weeks)

### 4.1 Distributed System Monitoring Dashboard 📈

**Score Impact**: Observability (7.8/10 → 9.0/10)  
**Effort**: 5-7 days

**Solution**:

Add Application Insights metrics:

```csharp
// src/DevOpsMigrationPlatform.Infrastructure/Telemetry/ApplicationInsightsMetrics.cs

public class MigrationMetrics
{
    private readonly TelemetryClient _telemetryClient;
    
    public void RecordJobStarted(string jobId, string mode)
    {
        _telemetryClient.TrackEvent("JobStarted", new Dictionary<string, string>
        {
            ["JobId"] = jobId,
            ["Mode"] = mode,
        });
    }
    
    public void RecordItemExported(string jobId, int itemId, TimeSpan duration)
    {
        _telemetryClient.TrackEvent("ItemExported", 
            new Dictionary<string, string> { ["JobId"] = jobId, ["ItemId"] = itemId.ToString() },
            new Dictionary<string, double> { ["DurationMs"] = duration.TotalMilliseconds });
    }
    
    public void RecordJobCompleted(string jobId, JobStatus status, TimeSpan totalDuration)
    {
        _telemetryClient.TrackEvent("JobCompleted",
            new Dictionary<string, string> { ["JobId"] = jobId, ["Status"] = status.ToString() },
            new Dictionary<string, double> { ["TotalDurationMs"] = totalDuration.TotalMilliseconds });
    }
}
```

**Enable Insights in ControlPlaneHost**:
```csharp
// Program.cs
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = configuration.GetConnectionString("ApplicationInsights");
});

builder.Services.AddSingleton<MigrationMetrics>();
```

**KQL Queries for Dashboard**:
```kusto
// Job Success Rate
customEvents
| where name == "JobCompleted"
| summarize SuccessCount = countif(tostring(customDimensions.Status) == "Success"),
            FailureCount = countif(tostring(customDimensions.Status) == "Failed")
            by bin(timestamp, 1h)
| project timestamp, SuccessCount, FailureCount, 
          SuccessRate = (SuccessCount * 100.0 / (SuccessCount + FailureCount))

// Export Throughput (items/minute)
customEvents
| where name == "ItemExported"
| summarize ItemCount = count() by bin(timestamp, 1m)
| project timestamp, ItemsPerMinute = ItemCount
```

---

### 4.2 Automated Rollback & Canary Deployment 🚀

**Effort**: 5-7 days

**Solution**: Extend Azure Container Apps deployment with:
- Canary traffic split (90% stable, 10% new version)
- Automatic rollback if error rate exceeds threshold
- Zero-downtime database migrations

---

## Implementation Roadmap

### Phase 1: Critical Security (Week 1)
- [ ] Remove all `Console.WriteLine` from production paths
- [ ] Implement WIQL injection validation
- [ ] Add exception credential masking
- [ ] Add SQL injection tests

**Estimated PR**: 1 day review

### Phase 2: High-Value Wins (Week 2)
- [ ] Implement WorkItems import
- [ ] Add health check endpoints
- [ ] Create operational runbooks
- [ ] Add structured logging with OpenTelemetry

**Estimated PR**: 2 days review

### Phase 3: Test Coverage (Week 3)
- [ ] Build E2E integration test suite
- [ ] Exception categorization framework
- [ ] Comprehensive CLI tests

**Estimated PR**: 2-3 days review

### Phase 4: Production Readiness (Week 4+)
- [ ] Monitoring dashboard
- [ ] Rollback automation
- [ ] Performance optimization

---

## Priority Matrix

```
High Impact, Low Effort (Do First):
├─ WIQL Injection Validation ⏱ 1 day
├─ Credential Masking ⏱ 1 day
├─ Remove Console.WriteLine ⏱ 2 days
└─ Health Checks ⏱ 2 days

High Impact, Medium Effort (Do Second):
├─ WorkItems Import ⏱ 3-5 days
├─ Structured Logging ⏱ 3-4 days
└─ Operational Runbooks ⏱ 1-2 days

Medium Impact, Medium Effort (Do Third):
├─ Integration Tests ⏱ 5-7 days
├─ Exception Categorization ⏱ 2-3 days
└─ SQL Injection Tests ⏱ 1 day

Medium Impact, High Effort (Backlog):
├─ Monitoring Dashboard ⏱ 5-7 days
└─ Canary Deployment ⏱ 5-7 days
```

---

## Success Metrics

After implementing these improvements:

| Current | Target | Metric |
|---------|--------|--------|
| 8.8/10  | 9.5+/10 | SOLID Score |
| 7.8/10  | 9.0/10  | Observability |
| 8.0/10  | 9.2/10  | Security |
| 8.5/10  | 9.5/10  | Testability |
| 7.9/10  | 9.0/10  | Documentation |

---

## Questions & Discussions

Before starting, clarify:

1. **WorkItems Import**: Is `IWorkItemImportSink` already in Abstractions, or do we need to create it?
2. **Health Checks**: Should they use Database Health Check from AspNetCore.Diagnostics?
3. **Runbooks**: Who is the audience (Ops team, SRE, Support)?
4. **Testing**: Should integration tests use real Azure DevOps or mocks?

