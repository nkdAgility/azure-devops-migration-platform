using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Infrastructure;

/// <summary>
/// Resolves Spectre.Console command types from the Microsoft DI container.
/// When a type is not registered (e.g. simple commands with no constructor dependencies)
/// Spectre.Console falls back to <see cref="Activator.CreateInstance"/>.
/// </summary>
internal sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider)
    {
        _provider = provider;
    }

    public object? Resolve(Type? type)
    {
        if (type is null) return null;
        return _provider.GetService(type);
    }

    public void Dispose()
    {
        if (_provider is IDisposable disposable)
            disposable.Dispose();
    }
}
