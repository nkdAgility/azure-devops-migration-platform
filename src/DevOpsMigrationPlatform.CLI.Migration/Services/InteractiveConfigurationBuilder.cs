using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using Spectre.Console;

namespace DevOpsMigrationPlatform.CLI.Migration.Services;

/// <summary>
/// Drives the interactive terminal wizard that builds a <see cref="MigrationOptions"/> from
/// user input. Contains no I/O or file concerns — accepts <see cref="IAnsiConsole"/> for
/// all output so the logic is testable independently of the CLI layer.
/// </summary>
internal sealed class InteractiveConfigurationBuilder : IInteractiveConfigurationBuilder
{
    public async Task<MigrationOptions> BuildAsync(IAnsiConsole console, CancellationToken cancellationToken)
    {
        var options = new MigrationOptions();

        // Step 1: Mode
        console.MarkupLine("[bold blue]Step 1: Migration Mode[/bold blue]");
        options.Mode = console.Prompt(
            new SelectionPrompt<string>()
                .Title("Select migration mode:")
                .AddChoices("Export", "Import", "Both"));
        console.WriteLine();

        var isExport = options.Mode is "Export" or "Both";
        var isImport = options.Mode is "Import" or "Both";

        // Step 2: Source (if Export or Both)
        if (isExport)
        {
            console.MarkupLine("[bold blue]Step 2: Source System Configuration[/bold blue]");
            options.Source = await ConfigureEndpointAsync(console, "source", cancellationToken);
            console.WriteLine();
        }

        // Step 3: Target (if Import or Both)
        if (isImport)
        {
            console.MarkupLine($"[bold blue]Step {(isExport ? 3 : 2)}: Target System Configuration[/bold blue]");
            options.Target = await ConfigureEndpointAsync(console, "target", cancellationToken);
            console.WriteLine();
        }

        // Step 4: Package path
        var stepNum = 2 + (isExport ? 1 : 0) + (isImport ? 1 : 0);
        console.MarkupLine($"[bold blue]Step {stepNum}: Package Storage[/bold blue]");
        options.Package.WorkingDirectory = console.Prompt(
            new TextPrompt<string>("Migration package directory:")
                .DefaultValue(options.Package.WorkingDirectory));
        options.Package.CreatePackage = console.Confirm("Compress the package (zip)?", defaultValue: false);

        return options;
    }

    private static Task<MigrationEndpointOptions> ConfigureEndpointAsync(
        IAnsiConsole console, string role, CancellationToken cancellationToken)
    {
        var endpoint = new AzureDevOpsEndpointOptions();

        var types = new[] { "AzureDevOpsServices", "TeamFoundationServer" };
        endpoint.Type = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Select {role} system type:")
                .AddChoices(types));

        var defaultUrl = endpoint.Type switch
        {
            "AzureDevOpsServices" => "https://dev.azure.com/[organization]",
            _ => "http://[server]:8080/tfs/[collection]"
        };

        endpoint.Url = console.Ask<string>($"Enter {role} URL [{defaultUrl}]:");
        endpoint.Project = console.Ask<string>($"Enter {role} project name:");

        var authTypes = new[] { "Pat", "Windows" };
        var authType = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Select {role} authentication type:")
                .AddChoices(authTypes));

        if (authType == "Pat")
        {
            var accessToken = console.Prompt(
                new TextPrompt<string>($"Enter {role} Personal Access Token (or $ENV:VARNAME):")
                    .Secret());

            endpoint.Authentication = new EndpointAuthenticationOptions
            {
                Type = AuthenticationType.Pat,
                AccessToken = accessToken
            };
        }
        else
        {
            endpoint.Authentication = new EndpointAuthenticationOptions { Type = AuthenticationType.Windows };
        }

        return Task.FromResult<MigrationEndpointOptions>(endpoint);
    }
}
