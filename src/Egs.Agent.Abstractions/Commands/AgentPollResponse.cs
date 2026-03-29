namespace Egs.Agent.Abstractions.Commands;

public sealed record AgentPollResponse(
    IReadOnlyList<ServerCommandMessage> Commands);