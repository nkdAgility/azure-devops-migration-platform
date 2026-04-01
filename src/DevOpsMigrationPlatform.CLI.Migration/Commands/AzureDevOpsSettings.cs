using Spectre.Console.Cli;
using System.ComponentModel;

namespace DevOpsMigrationPlatform.CLI.Commands;

public class AzureDevOpsSettings : CommandSettings
{
    [CommandOption("--organisation <ORGANISATION>")]
    [Description("The URL of the Azure DevOps organisation (e.g. https://dev.azure.com/myorg)")]
    public string Organisation { get; set; } = string.Empty;

    [CommandOption("--token <TOKEN>")]
    [Description("Personal access token for authentication")]
    public string Token { get; set; } = string.Empty;

    public override Spectre.Console.ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(Organisation))
            return Spectre.Console.ValidationResult.Error("--organisation is required");
        return Spectre.Console.ValidationResult.Success();
    }
}
