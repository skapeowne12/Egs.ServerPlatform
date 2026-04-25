namespace Egs.Contracts.Cloud;

public sealed record CloudControlOptions
{
    public bool Enabled { get; init; } = false;
    public string BaseUrl { get; init; } = "http://localhost:7071/";
    public string NodeKey { get; init; } = "windows-local-1";
    public string DisplayName { get; init; } = "Windows Local Node";
    public string OperatingSystem { get; init; } = "Windows";
    public int HeartbeatSeconds { get; init; } = 15;
}

public sealed record UpsertNodeRequest(
    string NodeKey,
    string DisplayName,
    string OperatingSystem,
    string MachineName);

public sealed record EgsNodeDto(
    Guid Id,
    string NodeKey,
    string DisplayName,
    string OperatingSystem,
    string MachineName,
    string Status,
    DateTimeOffset LastHeartbeatUtc,
    DateTimeOffset UpdatedUtc);

public sealed record UpsertCloudServerRequest(
    Guid ServerId,
    string Name,
    string GameKey,
    string InstallPath,
    string Status,
    string PublicStatus,
    string SettingsJson);