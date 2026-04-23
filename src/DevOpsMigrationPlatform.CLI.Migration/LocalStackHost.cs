using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Options;
using DevOpsMigrationPlatform.Infrastructure;
using DevOpsMigrationPlatform.Infrastructure.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Options;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.MigrationAgent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Starts the ControlPlane API and MigrationAgent worker for standalone CLI mode.
///
/// <para><b>Process-per-component mode (preferred):</b> When published ControlPlane and
/// MigrationAgent binaries are found on disk, each component is launched as a separate
/// child process via <see cref="ChildProcessHost"/>. This gives each component its own
/// <c>System.Diagnostics.DiagnosticListener</c> instance, eliminating the OpenTelemetry
/// instrumentation bleed that occurs when multiple OTel pipelines share a single process.
/// The Application Insights Application Map shows the correct topology:
/// <c>CLI ↔ ControlPlane ↔ Agent ↔ dev.azure.com</c>.</para>
///
/// <para><b>In-process fallback:</b> When the executables are not found (e.g. running via
/// <c>dotnet run</c> from source), falls back to hosting both components in-process using
/// the same service registrations as the standalone hosts. A warning is logged about
/// Application Map accuracy.</para>
///
/// See docs/cli.md — "Control Plane Endpoint".
/// </summary>
public sealed class LocalStackHost : IAsyncDisposable
{
    private readonly Uri _controlPlaneUrl;
    private readonly ILogger? _logger;

    // Process-per-component mode
    private ChildProcessHost? _controlPlaneProcess;
    private ChildProcessHost? _agentProcess;

    // In-process fallback mode
    private WebApplication? _controlPlaneInProcess;
    private IHost? _agentInProcess;

    private bool _isProcessMode;

    /// <summary>
    /// Creates a new <see cref="LocalStackHost"/> that binds the control plane to the given port.
    /// </summary>
    /// <param name="port">TCP port for the control plane API. Default: 5100.</param>
    /// <param name="logger">Optional logger for diagnostic messages.</param>
    public LocalStackHost(int port = 5100, ILogger? logger = null)
    {
        _controlPlaneUrl = new Uri($"http://localhost:{port}");
        _logger = logger;
    }

    /// <summary>
    /// Starts the ControlPlane and MigrationAgent, then waits for the ControlPlane
    /// to become healthy. Uses process-per-component when binaries are available,
    /// otherwise falls back to in-process hosting.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var controlPlaneExe = ChildProcessHost.ResolveExecutablePath("ControlPlane", "DevOpsMigrationPlatform.ControlPlaneHost");
        var agentExe = ChildProcessHost.ResolveExecutablePath("MigrationAgent", "DevOpsMigrationPlatform.MigrationAgent");

        if (controlPlaneExe is not null && agentExe is not null)
        {
            _isProcessMode = true;
            _logger?.LogInformation(
                "Starting standalone stack in process-per-component mode (ControlPlane: {CpExe}, Agent: {AgentExe})",
                controlPlaneExe, agentExe);

            await StartControlPlaneProcessAsync(controlPlaneExe, cancellationToken);
            await WaitForHealthyAsync(cancellationToken);
            StartAgentProcess(agentExe);
        }
        else
        {
            _isProcessMode = false;
            _logger?.LogWarning(
                "ControlPlane or MigrationAgent executables not found — falling back to in-process hosting. " +
                "Application Insights Application Map may show phantom dependency arrows. " +
                "To use process-per-component mode, publish the solution or run 'build.ps1 Install'.");

            await StartControlPlaneInProcessAsync(cancellationToken);
            await WaitForHealthyAsync(cancellationToken);
            await StartAgentInProcessAsync(cancellationToken);
        }
    }

    // ── Process-per-component mode ─────────────────────────────────────

    private async Task StartControlPlaneProcessAsync(string exePath, CancellationToken cancellationToken)
    {
        var env = BuildSharedEnvironment();
        env["ASPNETCORE_URLS"] = _controlPlaneUrl.ToString().TrimEnd('/');
        env["ASPNETCORE_ENVIRONMENT"] = "Development"; // Enables the auth bypass for local-only use

        _controlPlaneProcess = new ChildProcessHost("ControlPlane", exePath, env, _logger);
        _controlPlaneProcess.Start();

        // If the process exits immediately, something is wrong.
        if (_controlPlaneProcess.Exited is not null)
        {
            var raceTask = await Task.WhenAny(
                _controlPlaneProcess.Exited,
                Task.Delay(500, cancellationToken));
            if (raceTask == _controlPlaneProcess.Exited)
            {
                var exitCode = await _controlPlaneProcess.Exited;
                throw new InvalidOperationException(
                    $"ControlPlane process exited immediately with code {exitCode}. Check logs for details.");
            }
        }
    }

    private void StartAgentProcess(string exePath)
    {
        var env = BuildSharedEnvironment();
        env["ControlPlane__BaseUrl"] = _controlPlaneUrl.ToString().TrimEnd('/');
        // Note: Do NOT set DOTNET_ENVIRONMENT=Development for the Agent.
        // Development mode enables DI scope validation which rejects the
        // scoped IModule injection into singleton IHostedService. The Agent
        // is a worker service — it does not need the auth bypass middleware
        // that the ControlPlane uses in Development mode.

        _agentProcess = new ChildProcessHost("MigrationAgent", exePath, env, _logger);
        _agentProcess.Start();
    }

    /// <summary>
    /// Builds environment variables shared by both child processes: telemetry config,
    /// OTLP endpoint, and the CLI's appsettings.json location.
    /// </summary>
    private Dictionary<string, string> BuildSharedEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Forward Azure Monitor connection string from the CLI's loaded config
        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(appSettingsPath))
        {
            // Child processes load their own appsettings.json from their own directory,
            // but we also need to forward any CLI-level telemetry config that may not
            // be in their appsettings. Use environment variables (highest priority).
            try
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true)
                    .Build();

                var azMonConn = config["Telemetry:AzureMonitorConnectionString"];
                if (!string.IsNullOrEmpty(azMonConn))
                    env["Telemetry__AzureMonitorConnectionString"] = azMonConn;
            }
            catch
            {
                // Non-fatal — child processes will use their own config
            }
        }

        // Forward deployment context so child process OTel resource attributes
        // include the correct mode and control plane URL on Application Insights.
        env["MigrationPlatform__Environment__Type"] = "Standalone";
        env["MigrationPlatform__Environment__ControlPlane__BaseUrl"] = _controlPlaneUrl.ToString().TrimEnd('/');

        // Forward OTLP endpoint if set in the CLI's environment
        var otlp = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (!string.IsNullOrEmpty(otlp))
            env["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlp;

        return env;
    }

    // ── In-process fallback mode ───────────────────────────────────────

    private async Task StartControlPlaneInProcessAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();

        // Load the CLI's appsettings.json so Telemetry:AzureMonitorConnectionString
        // is available to ServiceDefaults (UseAzureMonitor) and other shared config.
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        builder.AddServiceDefaults(WellKnownServiceNames.ControlPlaneHost);

        // Filter customer-identifiable log data from the OTel pipeline (Azure Monitor).
        builder.Logging.AddDataClassificationFilter();

        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        // Register polymorphic serializers so MigrationEndpointOptions can be deserialized.
        builder.Services.AddEndpointOptionsType("AzureDevOpsServices", typeof(AzureDevOpsEndpointOptions));
        builder.Services.AddEndpointOptionsType("Simulated", typeof(SimulatedEndpointOptions));
        builder.Services.AddMigrationPlatformPolymorphicSerializers();

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ControlPlaneServiceExtensions).Assembly)
            .AddJsonOptions(opts =>
            {
                // Explicitly wire DefaultJsonTypeInfoResolver so that [JsonPolymorphic] /
                // [JsonDerivedType] attributes on Job are processed during [FromBody]
                // deserialization (required for abstract base-type binding in ASP.NET Core).
                opts.JsonSerializerOptions.TypeInfoResolver =
                    new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
                opts.JsonSerializerOptions.PropertyNamingPolicy =
                    System.Text.Json.JsonNamingPolicy.CamelCase;
                opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            });

        builder.Services.AddControlPlaneServices(builder.Configuration);

        // Post-configure ASP.NET JSON options to include the polymorphic endpoint converter.
        builder.Services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>(sp =>
        {
            var converter = sp.GetRequiredService<DevOpsMigrationPlatform.Infrastructure.Serialization.PolymorphicEndpointOptionsConverter>();
            return new Microsoft.Extensions.Options.PostConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>(
                string.Empty, opts => opts.JsonSerializerOptions.Converters.Add(converter));
        });

        builder.WebHost.UseUrls(_controlPlaneUrl.ToString().TrimEnd('/'));

        _controlPlaneInProcess = builder.Build();

        // Stamp every request as authenticated so the auth check in
        // ProgressController.GetLogs (403 for unauthenticated callers) passes.
        // LocalStackHost is single-user / local-only — no real auth is needed.
        _controlPlaneInProcess.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                var identity = new System.Security.Claims.ClaimsIdentity("LocalStack");
                identity.AddClaim(new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Name, "local-cli-user"));
                context.User = new System.Security.Claims.ClaimsPrincipal(identity);
            }
            await next();
        });

        _controlPlaneInProcess.MapControllers();

        await _controlPlaneInProcess.StartAsync(cancellationToken);
    }

    private async Task StartAgentInProcessAsync(CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();

        // Load the CLI's appsettings.json so Telemetry:AzureMonitorConnectionString
        // is available to ServiceDefaults (UseAzureMonitor) and other shared config.
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        builder.AddServiceDefaults(WellKnownServiceNames.MigrationAgent);

        // Filter customer-identifiable log data from the OTel pipeline (Azure Monitor).
        builder.Logging.AddDataClassificationFilter();

        // The CLI handles all user-facing console output via Spectre.Console and the
        // SSE progress stream. Suppress the default console logger entirely so internal
        // agent logs (Polly, JobAgentWorker, module diagnostics) do not leak to stdout.
        // Diagnostic logs still flow to PackageLoggerProvider (Logs/agent.jsonl) and
        // ControlPlaneLoggerProvider (diagnostics endpoint).
        builder.Logging.AddFilter<ConsoleLoggerProvider>(_ => false);

        builder.AddMigrationAgentServices(_controlPlaneUrl);

        _agentInProcess = builder.Build();
        await _agentInProcess.StartAsync(cancellationToken);
    }

    // ── Shared health check ────────────────────────────────────────────

    private async Task WaitForHealthyAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient { BaseAddress = _controlPlaneUrl };
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await http.GetAsync("/jobs", cancellationToken);
                if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
                    return;
            }
            catch (HttpRequestException)
            {
                // Not ready yet — keep polling
            }
            await Task.Delay(200, cancellationToken);
        }

        throw new TimeoutException(
            $"ControlPlane API at {_controlPlaneUrl} did not become ready within 10 seconds.");
    }

    // ── Disposal ───────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_isProcessMode)
        {
            await DisposeProcessModeAsync();
        }
        else
        {
            await DisposeInProcessModeAsync();
        }
    }

    private async ValueTask DisposeProcessModeAsync()
    {
        // Stop agent first (it polls the control plane), then control plane.
        if (_agentProcess is not null)
        {
            await _agentProcess.DisposeAsync();
            _agentProcess = null;
        }

        if (_controlPlaneProcess is not null)
        {
            await _controlPlaneProcess.DisposeAsync();
            _controlPlaneProcess = null;
        }
    }

    private async ValueTask DisposeInProcessModeAsync()
    {
        // Use a bounded timeout so Ctrl+C / ProcessExit never hangs.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            if (_agentInProcess is not null)
            {
                await _agentInProcess.StopAsync(timeoutCts.Token);
                _agentInProcess.Dispose();
                _agentInProcess = null;
            }

            if (_controlPlaneInProcess is not null)
            {
                await _controlPlaneInProcess.StopAsync(timeoutCts.Token);
                await _controlPlaneInProcess.DisposeAsync();
                _controlPlaneInProcess = null;
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout exceeded — forcibly dispose whatever we can.
            _agentInProcess?.Dispose();
            _agentInProcess = null;
            if (_controlPlaneInProcess is not null)
            {
                await _controlPlaneInProcess.DisposeAsync();
                _controlPlaneInProcess = null;
            }
        }
    }
}
