namespace Egs.Agent.Abstractions.Console;

public sealed record ConsoleLineMessage(
    Guid ServerId,
    DateTimeOffset TimestampUtc,
    string Line);