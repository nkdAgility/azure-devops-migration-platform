using DevOpsMigrationPlatform.CLI.Commands;
using OpenTelemetry.Trace;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DevOpsMigrationPlatform.CLI.Commands.Discovery;

public sealed class InventoryCommand : AsyncCommand<InventoryCommand.Settings>
{
    private readonly ActivitySource _activitySource;

    public InventoryCommand(ActivitySource activitySource)
    {
        _activitySource = activitySource;
    }

    public sealed class Settings : AzureDevOpsSettings
    {
        [CommandOption("--out <PATH>")]
        [Description("Write the inventory summary to a CSV file at this path")]
        public string? OutputPath { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        using var activity = _activitySource.StartActivity("inventory");
        try
        {
            return await RunCoreAsync(context, settings);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private async Task<int> RunCoreAsync(CommandContext context, Settings settings)
    {
        using var http = BuildClient(settings.Organisation, settings.Token);

        var projects = await FetchProjectsAsync(http, settings.Organisation);
        if (projects.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No projects found.[/]");
            return 0;
        }

        var summaries = new List<ProjectSummary>();

        await AnsiConsole.Live(BuildTable(summaries))
            .StartAsync(async ctx =>
            {
                foreach (var project in projects)
                {
                    var summary = new ProjectSummary { Name = project.Name, Id = project.Id };
                    summaries.Add(summary);
                    ctx.UpdateTarget(BuildTable(summaries));

                    summary.WorkItemCount = await CountWorkItemsAsync(http, settings.Organisation, project.Name);

                    ctx.UpdateTarget(BuildTable(summaries));
                }
            });

        AnsiConsole.MarkupLine("[green]✅ Discovery complete.[/]");

        if (!string.IsNullOrWhiteSpace(settings.OutputPath))
        {
            WriteCsv(summaries, settings.OutputPath);
            AnsiConsole.MarkupLineInterpolated($"Saved to [blue]{settings.OutputPath}[/]");
        }

        return 0;
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private static Table BuildTable(IReadOnlyList<ProjectSummary> summaries)
    {
        var table = new Table()
            .Title("[bold yellow]Inventory Progress[/]")
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Project")
            .AddColumn(new TableColumn("Work Items").RightAligned());

        foreach (var s in summaries)
        {
            var wi = s.WorkItemCount.HasValue ? s.WorkItemCount.Value.ToString() : "[grey]…[/]";
            table.AddRow(Markup.Escape(s.Name), wi);
        }

        return table;
    }

    // ── Azure DevOps REST helpers ─────────────────────────────────────────────

    private static HttpClient BuildClient(string organisation, string token)
    {
        var http = new HttpClient();
        http.BaseAddress = new Uri(organisation.TrimEnd('/') + "/");

        if (!string.IsNullOrWhiteSpace(token))
        {
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }

        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    private static async Task<List<(string Name, string Id)>> FetchProjectsAsync(HttpClient http, string organisation)
    {
        var uri = "_apis/projects?api-version=7.1&$top=500";
        var response = await http.GetAsync(uri);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var list = new List<(string, string)>();
        foreach (var item in root.GetProperty("value").EnumerateArray())
        {
            list.Add((item.GetProperty("name").GetString()!, item.GetProperty("id").GetString()!));
        }

        return list;
    }

    private static async Task<int> CountWorkItemsAsync(HttpClient http, string organisation, string projectName)
    {
        var body = JsonSerializer.Serialize(new { query = "SELECT [System.Id] FROM WorkItems" });
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var uri = $"{Uri.EscapeDataString(projectName)}/_apis/wit/wiql?api-version=7.1&$top=1";

        var response = await http.PostAsync(uri, content);
        if (!response.IsSuccessStatusCode)
            return -1;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("workItems", out var wi) ? wi.GetArrayLength() : 0;
    }

    // ── CSV export ───────────────────────────────────────────────────────────

    private static void WriteCsv(IReadOnlyList<ProjectSummary> summaries, string path)
    {
        using var writer = new StreamWriter(path, append: false, encoding: Encoding.UTF8);
        writer.WriteLine("Project,WorkItems");
        foreach (var s in summaries)
            writer.WriteLine($"{CsvEscape(s.Name)},{s.WorkItemCount ?? -1}");
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    // ── Model ─────────────────────────────────────────────────────────────────

    private sealed class ProjectSummary
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public int? WorkItemCount { get; set; }
    }
}
