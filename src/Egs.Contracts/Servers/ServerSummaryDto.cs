namespace Egs.Contracts.Servers;

public sealed record ServerSummaryDto(
    Guid Id,
    string Name,
    string GameKey,
    string NodeName,
    string Status,
    int? ProcessId);