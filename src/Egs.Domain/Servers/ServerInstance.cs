namespace Egs.Domain.Servers;

public sealed class ServerInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string GameKey { get; set; } = string.Empty;
    public string NodeName { get; set; } = "Local";
    public string Status { get; set; } = "Stopped";
    public int? ProcessId { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}