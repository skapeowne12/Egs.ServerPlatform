using Egs.Contracts.Servers;

namespace Egs.PluginSdk;

public sealed class PluginRenderContext
{
    public Guid ServerId { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public string GameKey { get; init; } = string.Empty;
    public string NodeName { get; init; } = string.Empty;
    public string InstallPath { get; init; } = string.Empty;
    public string InstallDirectory { get; init; } = string.Empty;
    public ServerSettingsDto Settings { get; init; } = new();
    public PluginDescriptor Plugin { get; init; } = new();
}
