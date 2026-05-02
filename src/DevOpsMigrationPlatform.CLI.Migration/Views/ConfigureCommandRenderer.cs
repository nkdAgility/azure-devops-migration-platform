// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using Spectre.Console;

namespace DevOpsMigrationPlatform.CLI.Migration.Views;

/// <summary>
/// Pure-rendering helper for the configure command.
/// Contains no domain or I/O logic — only Spectre.Console markup output.
/// </summary>
internal sealed class ConfigureCommandRenderer
{
    public void ShowBanner(IAnsiConsole console)
    {
        console.Write(new FigletText("Config Setup").Centered().Color(Color.Blue));
        console.Write(new Rule().RuleStyle("grey"));
        console.MarkupLine("[dim]Azure DevOps Migration Platform Configuration Wizard[/]");
        console.WriteLine();

        console.MarkupLine("This wizard will guide you through creating a migration configuration file.");
        console.MarkupLine("You can modify the generated file later or create additional configurations for different scenarios.");
        console.WriteLine();
    }

    public void ShowCompletionSummary(MigrationOptions options, string outputFile, IAnsiConsole console)
    {
        console.WriteLine();
        console.Write(new Rule("[bold green]Configuration Complete![/]").RuleStyle("green"));
        console.WriteLine();

        console.MarkupLine("[green]✓[/] Configuration file created successfully");
        console.MarkupLine($"[blue]📁 File:[/] {outputFile}");
        console.MarkupLine($"[blue]📋 Mode:[/] {options.Mode}");
        console.WriteLine();

        console.MarkupLine("[bold]Next Steps:[/]");
        console.MarkupLine($"• Run discovery: [cyan]devopsmigration discovery inventory --config {outputFile}[/]");

        if (options.Mode is "Export" or "Both")
            console.MarkupLine($"• Run export: [cyan]devopsmigration export --config {outputFile}[/]");

        if (options.Mode is "Import" or "Both")
            console.MarkupLine($"• Run import: [cyan]devopsmigration import --config {outputFile}[/]");

        console.WriteLine();
        console.MarkupLine("[dim]Tip: You can edit the configuration file directly to fine-tune settings.[/]");
    }
}
