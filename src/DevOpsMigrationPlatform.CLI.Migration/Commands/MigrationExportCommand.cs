using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.Infrastructure.Services;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Builds a <see cref="MigrationJob"/> from the migration config file and submits it
/// to the control plane via <see cref="IJobRunner"/> (ControlPlaneClient).
///
/// When <c>config.Source.Type == "TeamFoundationServer"</c> the command transparently
/// delegates to <see cref="TfsExportRunner"/> (the net481 subprocess bridge) instead of
/// submitting a job to the control plane. This keeps TFS OM handling invisible to the
/// operator: a single <c>devopsmigration export --config ...</c> command handles both
/// Azure DevOps Services and on-premises TFS/Azure DevOps Server.
///
/// No migration logic runs in this command — all execution happens in the agent (Azure DevOps path)
/// or the TFS subprocess (TFS path).
/// See docs/cli.md and system-architecture guardrail rules 16 and 19.
/// </summary>
public sealed class MigrationExportCommand : ControlPlaneCommandBase<MigrationExportCommandSettings>
{
    protected override async Task<int> ExecuteInternalAsync(
        CommandContext context,
        MigrationExportCommandSettings settings,
        CancellationToken cancellationToken = default)
    {
        var resolvedUrl = MigrationPlatformHost.ResolveControlPlaneUrl(settings.Url);

        await CreateHost(Environment.GetCommandLineArgs(), resolvedUrl, (services, config) =>
        {
            services.AddSingleton<IConfigurationService, ConfigurationService>();

            services.AddOptions<ControlPlaneOptions>()
                .BindConfiguration(ControlPlaneOptions.SectionName)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.AddHttpClient<ControlPlaneClient>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<ControlPlaneOptions>>().Value;
                client.BaseAddress = new Uri(opts.BaseUrl);
            });

            services.AddTransient<IJobRunner>(sp => sp.GetRequiredService<ControlPlaneClient>());

            // Pre-flight work item counting — uses the same date-window WIQL strategy as export.
            services.AddExportPreflightServices();

            // TFS subprocess services — registered unconditionally so DI resolves them
            // correctly when the source type is TeamFoundationServer.
            services.AddSingleton<IProgressSink, AnsiProgressSink>();
            services.AddSingleton<TfsExporterProcessAdapter>();
            services.AddSingleton<IExternalToolRunner, ExternalToolRunner>();
        });

        var config = await LoadConfigurationAsync(settings, cancellationToken);
        if (config is null)
            return 1;

        // Delegate to the TFS subprocess runner for on-premises sources.
        if (string.Equals(config.Source?.Type, "TeamFoundationServer", StringComparison.Ordinal))
            return await TfsExportRunner.RunAsync(config, Host!.Services, tfsExportExePathOverride: null, cancellationToken);

        return await ExecuteAdoExportAsync(config, settings, cancellationToken);
    }

    private async Task<int> ExecuteAdoExportAsync(MigrationOptions config, MigrationExportCommandSettings settings, CancellationToken cancellationToken)
    {
        var console = GetRequiredService<IAnsiConsole>();

        var orgUrl = config.Source?.ResolvedUrl;
        var project = config.Source?.Project;

        if (string.IsNullOrWhiteSpace(orgUrl))
        {
            ShowError(console, "Source.Url is required. Set it in the config file.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(project))
        {
            ShowError(console, "Source.Project is required. Set it in the config file.");
            return 1;
        }

        var outputPath = Path.GetFullPath(config.Artefacts.ExpandedPath);

        console.MarkupLine($"[blue]ℹ[/] Exporting from [bold]{Markup.Escape(orgUrl)}[/] / [bold]{Markup.Escape(project)}[/]");
        console.MarkupLine($"[blue]ℹ[/] Package path  : [blue]{Markup.Escape(outputPath)}[/]");

        var modules = BuildModules(config);

        // Pre-flight: count work items so we can show a deterministic progress bar.
        var pat = config.Source!.Authentication?.ResolvedAccessToken ?? string.Empty;
        var baseQuery = modules
            .FirstOrDefault(m => string.Equals(m.Name, "WorkItems", StringComparison.Ordinal))
            ?.Scopes.FirstOrDefault(s => string.Equals(s.Type, "wiql", StringComparison.Ordinal))
            ?.Parameters.GetValueOrDefault("query") as string;

        var totalWorkItems = 0;
        var discovery = GetRequiredService<IWorkItemDiscoveryService>();

        await console.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Plain)
            .StartAsync("[grey]Counting work items…[/]", async _ =>
            {
                await foreach (var snapshot in discovery.CountWorkItemsAsync(
                    orgUrl, project, pat, baseQuery, cancellationToken))
                {
                    if (snapshot.IsWorkItemComplete)
                        totalWorkItems = snapshot.WorkItemsCount;
                }
            });

        if (totalWorkItems > 0)
            console.MarkupLine($"[blue]ℹ[/] Work items found: [bold]{totalWorkItems:N0}[/]");

        // Build MigrationJob — no migration logic here.
        var job = new MigrationJob
        {
            JobId = Guid.NewGuid().ToString(),
            ConfigVersion = config.ConfigVersion,
            Mode = "Export",
            Source = new MigrationJobEndpoint
            {
                Type = config.Source!.Type,
                Url = orgUrl,
                Project = project,
                ApiVersion = config.Source.ApiVersion,
                Authentication = config.Source.Authentication
            },
            Artefacts = new MigrationJobArtefacts
            {
                PackageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}",
                Zip = config.Artefacts.Zip
            },
            Modules = modules,
            Guardrails = new MigrationJobGuardrails
            {
                StreamingRequired = true,
                CanonicalWorkItemsLayoutRequired = true
            },
            Diagnostics = new MigrationJobDiagnostics
            {
                MinimumLevel = settings.Level
            },
            Resume = settings.ForceFresh
                ? new MigrationJobResume { Mode = ResumeMode.ForceFresh }
                : null
        };

        // Determine follow mode: explicit --follow, or implicit in standalone mode (no --url).
        var isStandaloneMode = string.IsNullOrEmpty(settings.Url);
        var shouldFollow = settings.Follow || isStandaloneMode;

        var client = GetRequiredService<ControlPlaneClient>();

        // Non-follow remote mode: submit and exit immediately (FR-025).
        if (!shouldFollow)
        {
            var jobId = await client.SubmitAsync(job, cancellationToken);
            var resolvedControlPlaneUrl = MigrationPlatformHost.ResolveControlPlaneUrl(settings.Url) ?? "http://localhost:5100";
            PrintJobSubmitted(console, jobId, resolvedControlPlaneUrl);
            console.MarkupLine($"[grey]Use [blue]manage status --job {jobId}[/] to check progress.[/]");
            return 0;
        }

        // Follow mode: stream progress + diagnostics concurrently.
        var parsedJobId = Guid.Parse(job.JobId);
        ProgressEvent? lastEvt = null;
        var jobFailed = false;

        // Use a linked CTS that we cancel on Ctrl+C to detach from diagnostics
        // without cancelling the job.
        using var followCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Register Ctrl+C handler for graceful detach.
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // Prevent process exit.
            followCts.Cancel();
        };

        // Submit the job first.
        try
        {
            parsedJobId = await client.SubmitAsync(job, cancellationToken);
            var resolvedControlPlaneUrl = MigrationPlatformHost.ResolveControlPlaneUrl(settings.Url) ?? "http://localhost:5100";
            PrintJobSubmitted(console, parsedJobId, resolvedControlPlaneUrl);
        }
        catch (Exception ex)
        {
            ShowError(console, $"Failed to submit job: {ex.Message}");
            return 1;
        }

        // Diagnostics records are buffered while the Progress() renderer owns the
        // console, then flushed afterwards.  Writing directly to the console while
        // Progress() is active causes rendering artefacts because Spectre's progress
        // renderer is not thread-safe with concurrent console writers.
        var diagnosticsBuffer = new System.Collections.Concurrent.ConcurrentQueue<string>();

        // Start diagnostics streaming as a background task — enqueue, don't print.
        var diagnosticsTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var record in client.StreamDiagnosticsAsync(parsedJobId, settings.Level, followCts.Token))
                {
                    var levelColor = record.Level switch
                    {
                        "Error" or "Critical" => "red",
                        "Warning" => "yellow",
                        "Debug" or "Trace" => "grey",
                        _ => "blue"
                    };
                    diagnosticsBuffer.Enqueue(
                        $"[{levelColor}]{Markup.Escape(record.Level)}[/] [{Markup.Escape(record.Category)}] {Markup.Escape(record.Message)}");
                }
            }
            catch (OperationCanceledException)
            {
                // Detach — user cancelled follow.
            }
            catch (Exception)
            {
                // Best-effort diagnostics streaming — don't propagate.
            }
        }, followCts.Token);

        var progressStartTime = DateTimeOffset.UtcNow;
        int lastWiId = 0;
        int wiRevisions = 0;
        int wiStartRevisions = 0;
        int prevRevisions = 0;
        int lastCompletedWiId = 0;
        int lastCompletedRevisions = 0;
        string currentStage = string.Empty;

        if (Console.IsOutputRedirected)
        {
            // Non-interactive (redirected stdout — subprocess, CI, test runner): skip the
            // Live renderer entirely.  Cursor-positioning ANSI sequences throw "The handle
            // is invalid" on non-console handles; plain event iteration is sufficient.
            try
            {
                await foreach (var evt in client.FollowLogsAsync(parsedJobId, followCts.Token).ConfigureAwait(false))
                {
                    lastEvt = evt;
                    if (!string.IsNullOrEmpty(evt.Stage))
                        currentStage = evt.Stage;
                    if (evt.WorkItemId != 0 && evt.WorkItemId != lastWiId)
                    {
                        if (lastWiId != 0)
                        {
                            lastCompletedWiId = lastWiId;
                            lastCompletedRevisions = prevRevisions - wiStartRevisions;
                        }
                        lastWiId = evt.WorkItemId;
                        wiStartRevisions = prevRevisions;
                    }
                    if (evt.WorkItemId != 0)
                        wiRevisions = evt.RevisionsProcessed - wiStartRevisions;
                    prevRevisions = evt.RevisionsProcessed;
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Job failed"))
            {
                jobFailed = true;
                ShowError(console, ex.Message);
                if (lastEvt is not null)
                    ShowError(console, $"Last progress: {lastEvt.WorkItemsProcessed} work items / {lastEvt.RevisionsProcessed} revisions (wi#{lastEvt.WorkItemId})");
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }
        else
        {
            // Interactive path: redirect Console.Out to prevent ILogger output from
            // interleaving with the Live renderer, then render the progress bar.
            //
            // AnsiConsole.Console captured its TextWriter at creation time, so it always
            // writes to the real terminal regardless of Console.SetOut().  The .NET ILogger
            // ConsoleLogger calls Console.Out dynamically on each write — so redirecting
            // Console.Out to a buffer prevents any logger output from interleaving with the
            // Live renderer.  The buffer is flushed after Live() exits.
            var logBuffer = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(logBuffer);

            try
            {
                await console.Live(BuildProgressRenderable(
                        0, totalWorkItems, 0, 0, 0, 0, string.Empty, progressStartTime))
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Ellipsis)
                    .StartAsync(async ctx =>
                    {
                        await foreach (var evt in client.FollowLogsAsync(parsedJobId, followCts.Token).ConfigureAwait(false))
                        {
                            lastEvt = evt;

                            if (!string.IsNullOrEmpty(evt.Stage))
                                currentStage = evt.Stage;

                            if (evt.WorkItemId != 0 && evt.WorkItemId != lastWiId)
                            {
                                // The previous WI just completed — record it for the completed row.
                                if (lastWiId != 0)
                                {
                                    lastCompletedWiId = lastWiId;
                                    lastCompletedRevisions = prevRevisions - wiStartRevisions;
                                }
                                lastWiId = evt.WorkItemId;
                                wiStartRevisions = prevRevisions;
                            }

                            if (evt.WorkItemId != 0)
                                wiRevisions = evt.RevisionsProcessed - wiStartRevisions;

                            prevRevisions = evt.RevisionsProcessed;

                            ctx.UpdateTarget(BuildProgressRenderable(
                                evt.WorkItemsProcessed, totalWorkItems,
                                lastWiId, wiRevisions,
                                lastCompletedWiId, lastCompletedRevisions,
                                currentStage, progressStartTime));
                        }
                    });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Job failed"))
            {
                jobFailed = true;
                ShowError(console, ex.Message);
                if (lastEvt is not null)
                    ShowError(console, $"Last progress: {lastEvt.WorkItemsProcessed} work items / {lastEvt.RevisionsProcessed} revisions (wi#{lastEvt.WorkItemId})");
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C pressed — detach.
                console.MarkupLine("[yellow]Detached from diagnostic stream. Job continues running on the server.[/]");
                console.MarkupLine("[grey]Use [bold]tui[/] to resume watching.[/]");
                return 0;
            }
            finally
            {
                // Restore stdout and flush anything the Console logger wrote during the live render.
                Console.SetOut(originalOut);
                var captured = logBuffer.ToString();
                logBuffer.Dispose();
                if (!string.IsNullOrWhiteSpace(captured))
                    console.Write(new Text(captured.TrimEnd()));
            }
        }

        // Stop diagnostics stream.
        await followCts.CancelAsync();
        try { await diagnosticsTask; } catch (OperationCanceledException) { }

        // Flush buffered diagnostics now that the Progress() renderer has released the console.
        while (diagnosticsBuffer.TryDequeue(out var line))
            console.MarkupLine(line);

        if (jobFailed)
            return 1;

        if (lastEvt is not null)
            ShowSuccess(console, $"Export complete — {lastEvt.WorkItemsProcessed} work items / {lastEvt.RevisionsProcessed} revisions written to package.");
        else
            ShowSuccess(console, "Work item export complete.");
        return 0;
    }

    /// <summary>
    /// Builds the fixed 3-row Live renderable:
    ///   Row 1 — overall WorkItems progress bar
    ///   Row 2 — last completed work item (full bar, greyed)
    ///   Row 3 — current work item in progress (partial bar)
    /// Row count is always exactly 3 so Live()'s cursor-up stays stable.
    /// </summary>
    private static IRenderable BuildProgressRenderable(
        int processed, int total,
        int currentWiId, int currentWiRevisions,
        int lastCompletedWiId, int lastCompletedRevisions,
        string stage, DateTimeOffset startTime)
    {
        const int BarWidth = 38;
        const int WiBarWidth = 20;

        // ── Row 1: overall progress ──────────────────────────────────────────────────
        var pct = total > 0 ? (double)processed / total : 0.0;
        var filled = Math.Clamp((int)(pct * BarWidth), 0, BarWidth);
        var overallBar = new string('━', filled) + new string('─', BarWidth - filled);
        var stageStr = string.IsNullOrEmpty(stage) ? string.Empty : $"  [grey]{Markup.Escape(stage)}[/]";
        var etaStr = ComputeEta(startTime, processed, total);
        var overall = new Markup(
            $"[bold]WorkItems[/]{stageStr}  [blue]{Markup.Escape(overallBar)}[/]" +
            $"  [bold]{processed:N0}[/][grey]/{total:N0}[/]" +
            $"  [grey]{pct * 100.0:F1}%[/]  [grey]ETA: {Markup.Escape(etaStr)}[/]");

        // ── Row 2: last completed WI (full bar = 100 %) ──────────────────────────────
        IRenderable completedRow = lastCompletedWiId > 0
            ? new Markup($"  [grey]✓ WI {lastCompletedWiId}  {new string('━', WiBarWidth)}  {lastCompletedRevisions} rev  done[/]")
            : new Markup("  [grey]─[/]");

        // ── Row 3: current WI in progress (partial bar, never reaches 100 %) ─────────
        IRenderable currentRow;
        if (currentWiId > 0)
        {
            // Bar fills as revisions arrive; capped one below full so it never
            // reads as 100 % while the item is still being processed.
            var wiBarFilled = Math.Min(currentWiRevisions, WiBarWidth - 1);
            var wiBar = new string('━', wiBarFilled) + new string('─', WiBarWidth - wiBarFilled);
            currentRow = new Markup(
                $"  [grey]↳[/] WI [bold]{currentWiId}[/]  [blue]{Markup.Escape(wiBar)}[/]  [grey]{currentWiRevisions} rev[/]");
        }
        else
        {
            currentRow = new Markup("  [grey]↳ waiting…[/]");
        }

        return new Rows(overall, completedRow, currentRow);
    }

    private static string ComputeEta(DateTimeOffset startTime, int processed, int total)
    {
        if (processed <= 0 || total <= 0) return "--:--:--";
        var elapsedSecs = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
        if (elapsedSecs < 1) return "--:--:--";
        var remainingSecs = (total - processed) / (processed / elapsedSecs);
        var eta = TimeSpan.FromSeconds(remainingSecs);
        return eta.TotalHours >= 1
            ? $"{(int)eta.TotalHours}:{eta.Minutes:D2}:{eta.Seconds:D2}"
            : $"--:{eta.Minutes:D2}:{eta.Seconds:D2}";
    }

    /// <summary>
    /// Converts <see cref="MigrationOptions.Modules"/> into <see cref="MigrationJobModule"/> entries.
    /// If the config has no modules, a default WorkItems/wiql scope is injected to preserve
    /// backward compatibility with pre-modules config files.
    /// </summary>
    private static List<MigrationJobModule> BuildModules(MigrationOptions config)
    {
        if (config.Modules.Count > 0)
        {
            return config.Modules
                .Where(m => m.Enabled)
                .Select(m => new MigrationJobModule
                {
                    Name = m.Name,
                    Scopes = m.Scopes
                        .Select(s => new MigrationJobModuleScope
                        {
                            Type = s.Type,
                            Parameters = s.Parameters
                                .ToDictionary(
                                    kv => kv.Key,
                                    kv => (object?)kv.Value.ToString())
                        })
                        .ToList()
                })
                .ToList();
        }

        // Default: WorkItems module with platform-default WIQL scope.
        return
        [
            new MigrationJobModule
            {
                Name   = "WorkItems",
                Scopes =
                [
                    new MigrationJobModuleScope
                    {
                        Type       = "wiql",
                        Parameters = new Dictionary<string, object?>
                        {
                            ["query"]              = Infrastructure.Modules.WorkItemsScopeParameters.DefaultWiqlQuery,
                            ["includeRevisions"]   = true,
                            ["includeLinks"]       = true,
                            ["includeAttachments"] = true
                        }
                    }
                ]
            }
        ];
    }
}

