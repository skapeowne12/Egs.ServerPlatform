namespace Egs.Agent.Abstractions.Status;

public sealed record AgentServerUpdateMessage(
    Guid ServerId,
    string Status,
    int? ProcessId,
    string? Error,
    DateTimeOffset UpdatedUtc);