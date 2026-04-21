namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Process-wide cancellation signal wired to <c>Console.CancelKeyPress</c> and
/// <c>AppDomain.ProcessExit</c> in <see cref="Program.Main"/>.
/// Commands link their per-invocation token to this via
/// <see cref="System.Threading.CancellationTokenSource.CreateLinkedTokenSource"/>.
/// </summary>
internal static class GlobalCancellation
{
    private static readonly CancellationTokenSource Cts = new();

    /// <summary>Fires when Ctrl+C or process exit is detected.</summary>
    public static CancellationToken Token => Cts.Token;

    /// <summary>Signal cancellation. Safe to call more than once.</summary>
    public static void Cancel()
    {
        if (!Cts.IsCancellationRequested)
            Cts.Cancel();
    }
}
