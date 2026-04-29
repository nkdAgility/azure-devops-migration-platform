using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Implemented by background services that buffer data asynchronously and need an explicit
/// flush before the owning job completes. Callers depend on this abstraction rather than
/// on concrete infrastructure types.
/// </summary>
public interface IFlushable
{
    /// <summary>
    /// Drains all buffered data to the backing store. Safe to call more than once.
    /// </summary>
    Task FlushAsync();
}
