using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Egs.PluginSdk;

public sealed class FileSystemPluginCatalog : IPluginCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    private readonly IReadOnlyDictionary<string, PluginDescriptor> _plugins;

    public FileSystemPluginCatalog(PluginCatalogOptions options, ILogger<FileSystemPluginCatalog> logger)
    {
        var rootPath = string.IsNullOrWhiteSpace(options.RootPath)
            ? Path.Combine(AppContext.BaseDirectory, "plugins")
            : Path.GetFullPath(options.RootPath);

        if (!Directory.Exists(rootPath))
        {
            logger.LogWarning("Plugin root '{PluginRoot}' does not exist. No plugins were loaded.", rootPath);
            _plugins = new Dictionary<string, PluginDescriptor>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var loaded = new Dictionary<string, PluginDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifestPath in Directory.EnumerateFiles(rootPath, "plugin.json", SearchOption.AllDirectories))
        {
            var descriptor = LoadDescriptor(manifestPath);
            if (string.IsNullOrWhiteSpace(descriptor.Key))
            {
                descriptor.Key = Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? string.Empty;
            }

            descriptor.SourceDirectory = Path.GetDirectoryName(manifestPath) ?? rootPath;
            loaded[descriptor.Key] = descriptor;
            logger.LogInformation("Loaded plugin '{PluginKey}' from {ManifestPath}.", descriptor.Key, manifestPath);
        }

        _plugins = loaded;
    }

    public IReadOnlyList<PluginDescriptor> GetAll() =>
        _plugins.Values
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public PluginDescriptor? TryGet(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return _plugins.TryGetValue(key.Trim(), out var plugin) ? plugin : null;
    }

    public PluginDescriptor GetRequired(string key) =>
        TryGet(key) ?? throw new KeyNotFoundException($"Plugin '{key}' was not found.");

    private static PluginDescriptor LoadDescriptor(string manifestPath)
    {
        using var stream = File.OpenRead(manifestPath);
        return JsonSerializer.Deserialize<PluginDescriptor>(stream, JsonOptions)
               ?? throw new InvalidOperationException($"Plugin manifest '{manifestPath}' could not be deserialized.");
    }
}
