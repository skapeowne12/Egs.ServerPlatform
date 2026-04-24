using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Egs.PluginSdk;

public static class PluginServiceCollectionExtensions
{
    public static IServiceCollection AddEgsPluginCatalog(this IServiceCollection services, IConfiguration configuration)
    {
        var configuredRoot = configuration["Plugins:RootPath"];
        var rootPath = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(AppContext.BaseDirectory, "plugins")
            : configuredRoot;

        services.TryAddSingleton(new PluginCatalogOptions
        {
            RootPath = rootPath
        });

        services.TryAddSingleton<IPluginCatalog, FileSystemPluginCatalog>();
        return services;
    }
}
