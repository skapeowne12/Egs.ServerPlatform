namespace Egs.Domain.Servers;

public sealed class ServerCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public string Type { get; set; } = string.Empty; // Start, Stop, Restart
    public string Status { get; set; } = "Pending";  // Pending, Claimed, Completed, Failed
    public string NodeName { get; set; } = "Local";
    public string? Error { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClaimedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}