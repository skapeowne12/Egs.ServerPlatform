namespace Egs.Agent.Abstractions.Status;

public sealed record ServerStatusChangedMessage(
    Guid ServerId,
    string Name,
    string Status,
    int? ProcessId,
    DateTimeOffset UpdatedUtc);