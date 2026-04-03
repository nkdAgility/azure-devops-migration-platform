using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Infrastructure;

/// <summary>
/// Bridges Microsoft.Extensions.DependencyInjection into Spectre.Console.Cli so that
/// commands and their dependencies are resolved from the same DI container that holds
/// <see cref="Microsoft.Extensions.Options.IOptions{T}"/>, <c>IConfiguration</c>, etc.
///
/// Usage:
/// <code>
/// var registrar = new TypeRegistrar(services);
/// var app = new CommandApp(registrar);
/// </code>
///
/// See https://spectreconsole.net/cli/di
/// </summary>
internal sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;

    public TypeRegistrar(IServiceCollection services)
    {
        _services = services;
    }

    public ServiceProvider? BuiltServiceProvider { get; private set; }

    public ITypeResolver Build()
    {
        BuiltServiceProvider = _services.BuildServiceProvider();
        return new TypeResolver(BuiltServiceProvider);
    }

    public void Register(Type service, Type implementation) =>
        _services.AddSingleton(service, implementation);

    public void RegisterInstance(Type service, object implementation) =>
        _services.AddSingleton(service, implementation);

    public void RegisterLazy(Type service, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _services.AddSingleton(service, _ => factory());
    }
}
