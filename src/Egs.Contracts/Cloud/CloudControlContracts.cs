namespace Egs.Contracts.Cloud;

public sealed record CloudControlOptions
{
    public bool Enabled { get; init; } = false;
    public string BaseUrl { get; init; } = "http://localhost:7071/";
    public string NodeKey { get; init; } = "windows-local-1";
    public string DisplayName { get; init; } = "Windows Local Node";
    public string OperatingSystem { get; init; } = "Windows";
    public int HeartbeatSeconds { get; init; } = 15;
    public int ServerSyncSeconds { get; init; } = 30;
    public int CommandPollSeconds { get; init; } = 5;
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

public sealed record CloudServerCommandDto(
    Guid Id,
    Guid NodeId,
    Guid ServerId,
    string CommandType,
    string Status,
    string PayloadJson,
    string? ResultJson,
    string? ErrorMessage,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc);

public sealed record UpdateCloudCommandStatusRequest(
    string Status,
    string? ResultJson,
    string? ErrorMessage);

public sealed record PushCloudServerStatusRequest(
    string Status,
    int? ProcessId,
    int? PlayerCount,
    int? MaxPlayers,
    string? MetadataJson);

public sealed record CloudConsoleLineDto(
    long Id,
    Guid NodeId,
    Guid ServerId,
    string Line,
    string Level,
    DateTimeOffset RecordedUtc);

public sealed record PushCloudConsoleLinesRequest(
    IReadOnlyList<PushCloudConsoleLineItem> Lines);

public sealed record PushCloudConsoleLineItem(
    string Line,
    string? Level,
    DateTimeOffset? RecordedUtc);

public sealed record ConsoleCommandPayload(
    string Input);

public sealed record CreateNodeDeployTokenRequest(
    string NodeKey,
    string DisplayName,
    string OperatingSystem,
    int ExpiresHours,
    string? AgentPackageUrl,
    string? ApiPackageUrl);

public sealed record NodeDeployTokenDto(
    Guid Id,
    string Token,
    string NodeKey,
    string DisplayName,
    string OperatingSystem,
    DateTimeOffset ExpiresUtc,
    string? AgentPackageUrl,
    string? ApiPackageUrl,
    string WindowsBootstrapScript,
    string LinuxBootstrapScript);
