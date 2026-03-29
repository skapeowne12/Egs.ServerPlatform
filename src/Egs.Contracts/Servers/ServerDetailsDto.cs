namespace Egs.Contracts.Servers;

public sealed class ServerDetailsDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GameKey { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ProcessId { get; set; }
    public ServerSettingsDto Settings { get; set; } = new();
}