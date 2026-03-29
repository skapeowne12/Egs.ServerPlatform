namespace Egs.Agent.Abstractions.Commands;

public sealed record ServerCommandMessage(
    Guid CommandId,
    Guid ServerId,
    ServerCommandType Type,
    DateTimeOffset RequestedUtc);