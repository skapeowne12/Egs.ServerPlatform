namespace Egs.Contracts.Servers;

public sealed class ServerDetailsDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GameKey { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ProcessId { get; set; }

    public string? IpAddress { get; set; }
    public int? Port { get; set; }
    public int? QueryPort { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }

    public string PluginDisplayName { get; set; } = string.Empty;
    public List<ServerMetadataItemDto> PluginMetadata { get; set; } = [];

    public ServerSettingsDto Settings { get; set; } = new();
}