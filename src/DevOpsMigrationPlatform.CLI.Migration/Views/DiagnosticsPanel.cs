using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using Spectre.Console;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Renders a diagnostics panel for a running job using Spectre.Console.
/// Displays the most recent <see cref="DiagnosticLogRecord"/> entries
/// received from the control plane SSE stream.
/// Call <see cref="Add"/> to append new records and <see cref="Render"/>
/// to print the panel.
/// </summary>
public sealed class DiagnosticsPanel
{
    private const int MaxVisibleRecords = 20;
    private readonly List<DiagnosticLogRecord> _records = new();
    private readonly object _lock = new();

    /// <summary>Adds a record to the panel buffer. Thread-safe.</summary>
    public void Add(DiagnosticLogRecord record)
    {
        lock (_lock)
        {
            _records.Add(record);
            while (_records.Count > MaxVisibleRecords)
                _records.RemoveAt(0);
        }
    }

    /// <summary>Writes the panel to the given console.</summary>
    public void Render(IAnsiConsole console)
    {
        DiagnosticLogRecord[] snapshot;
        lock (_lock)
        {
            snapshot = _records.ToArray();
        }

        var panel = new Panel(BuildContent(snapshot))
        {
            Header = new PanelHeader($"Diagnostics (as of {DateTimeOffset.UtcNow:HH:mm:ss})"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        };

        console.Write(panel);
    }

    private static string BuildContent(DiagnosticLogRecord[] records)
    {
        if (records.Length == 0)
            return "[grey](no diagnostics yet…)[/]";

        var lines = new List<string>(records.Length);
        foreach (var record in records)
        {
            var levelColor = record.Level switch
            {
                "Error" or "Critical" => "red",
                "Warning" => "yellow",
                "Debug" or "Trace" => "grey",
                _ => "blue"
            };

            lines.Add(
                $"[grey]{record.Timestamp:HH:mm:ss.fff}[/] [{levelColor}]{Markup.Escape(record.Level),-12}[/] {Markup.Escape(record.Message)}");
        }

        return string.Join("\n", lines);
    }
}
