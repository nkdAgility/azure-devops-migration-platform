#if !NET481
using Microsoft.Extensions.DependencyInjection;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityLookup;

public static class IdentityLookupToolServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityLookupToolServices(this IServiceCollection services)
    {
        services.AddOptions<IdentityLookupOptions>()
            .BindConfiguration(IdentityLookupOptions.SectionName);
        services.AddSingleton<IdentityLookupTool>();
        services.AddSingleton<IIdentityLookupTool>(sp => sp.GetRequiredService<IdentityLookupTool>());
        return services;
    }
}
#endif
