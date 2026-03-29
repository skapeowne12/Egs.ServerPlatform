namespace Egs.Agent.Abstractions.Status;

public sealed record AgentServerUpdateMessage(
    Guid? CommandId,
    Guid ServerId,
    string Status,
    int? ProcessId,
    string? Error,
    DateTimeOffset UpdatedUtc);
