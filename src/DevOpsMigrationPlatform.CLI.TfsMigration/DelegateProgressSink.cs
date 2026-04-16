using System;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.CLI.TfsMigration;

/// <summary>
/// Simple <see cref="IProgressSink"/> backed by an <see cref="Action{T}"/>.
/// Avoids a separate class file for one-off visual rendering callbacks.
/// </summary>
internal sealed class DelegateProgressSink : IProgressSink
{
    private readonly Action<ProgressEvent> _handler;

    public DelegateProgressSink(Action<ProgressEvent> handler)
        => _handler = handler ?? throw new ArgumentNullException(nameof(handler));

    public void Emit(ProgressEvent evt) => _handler(evt);
}
