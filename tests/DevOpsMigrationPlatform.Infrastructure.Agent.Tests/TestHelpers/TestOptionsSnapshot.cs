using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestHelpers;

/// <summary>
/// Minimal <see cref="IOptionsSnapshot{TOptions}"/> implementation for unit tests.
/// Returns the same fixed value from both <see cref="Value"/> and <see cref="Get"/>.
/// Use instead of <c>Options.Create()</c> wherever a constructor requires
/// <c>IOptionsSnapshot&lt;T&gt;</c>.
/// </summary>
internal sealed class TestOptionsSnapshot<T>(T value) : IOptionsSnapshot<T> where T : class
{
    public T Value => value;
    public T Get(string? name) => value;
}
