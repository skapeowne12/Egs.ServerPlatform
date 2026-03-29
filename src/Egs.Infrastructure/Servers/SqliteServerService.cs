using System.Text.Json;
using System.Text.RegularExpressions;
using Egs.Agent.Abstractions.Status;
using Egs.Application.Servers;
using Egs.Contracts.Servers;
using Egs.Domain.Servers;
using Egs.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Egs.Infrastructure.Servers;

public sealed class SqliteServerService : IServerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex InvalidPathCharsRegex = new($"[{Regex.Escape(new string(Path.GetInvalidFileNameChars()))}]", RegexOptions.Compiled);

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IServerStatusNotifier _statusNotifier;

    public SqliteServerService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IServerStatusNotifier statusNotifier)
    {
        _dbContextFactory = dbContextFactory;
        _statusNotifier = statusNotifier;
    }

    public async Task<IReadOnlyList<ServerSummaryDto>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        return await db.Servers
            .OrderBy(x => x.Name)
            .Select(x => new ServerSummaryDto(
                x.Id,
                x.Name,
                x.GameKey,
                x.NodeName,
                x.Status,
                x.ProcessId))
            .ToListAsync(ct);
    }

    public async Task<ServerSummaryDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        return await db.Servers
            .Where(x => x.Id == id)
            .Select(x => new ServerSummaryDto(
                x.Id,
                x.Name,
                x.GameKey,
                x.NodeName,
                x.Status,
                x.ProcessId))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<ServerDetailsDto?> GetDetailsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (server is null)
            return null;

        return new ServerDetailsDto
        {
            Id = server.Id,
            Name = server.Name,
            GameKey = server.GameKey,
            NodeName = server.NodeName,
            InstallPath = server.InstallPath,
            Status = server.Status,
            ProcessId = server.ProcessId,
            Settings = DeserializeSettings(server.SettingsJson)
        };
    }

    public async Task<Guid> CreateAsync(CreateServerRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Server name is required.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.GameKey))
            throw new ArgumentException("Game key is required.", nameof(request));

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var serverId = Guid.NewGuid();
        var normalizedName = request.Name.Trim();
        var normalizedGameKey = request.GameKey.Trim();

        var defaultSettings = BuildDefaultSettings(normalizedName, normalizedGameKey);

        var server = new ServerInstance
        {
            Id = serverId,
            Name = normalizedName,
            GameKey = normalizedGameKey,
            NodeName = string.IsNullOrWhiteSpace(request.NodeName) ? "Local" : request.NodeName.Trim(),
            InstallPath = NormalizeInstallPath(request.InstallPath, normalizedGameKey, normalizedName, serverId),
            Status = "Stopped",
            SettingsJson = SerializeSettings(defaultSettings),
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        db.Servers.Add(server);
        await db.SaveChangesAsync(ct);

        return server.Id;
    }

    public async Task UpdateSettingsAsync(Guid id, ServerSettingsDto settings, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (server is null)
            throw new KeyNotFoundException($"Server '{id}' was not found.");

        server.SettingsJson = SerializeSettings(settings);
        server.UpdatedUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public Task InstallAsync(Guid id, CancellationToken ct = default)
        => QueueCommandAsync(id, "Install", "Installing", ct);

    public Task StartAsync(Guid id, CancellationToken ct = default)
        => QueueCommandAsync(id, "Start", "Starting", ct);

    public Task StopAsync(Guid id, CancellationToken ct = default)
        => QueueCommandAsync(id, "Stop", "Stopping", ct);

    public Task RestartAsync(Guid id, CancellationToken ct = default)
        => QueueCommandAsync(id, "Restart", "Restarting", ct);

    private async Task QueueCommandAsync(
        Guid serverId,
        string commandType,
        string pendingStatus,
        CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == serverId, ct);
        if (server is null)
            throw new KeyNotFoundException($"Server '{serverId}' was not found.");

        server.Status = pendingStatus;
        server.UpdatedUtc = DateTimeOffset.UtcNow;

        var command = new ServerCommand
        {
            Id = Guid.NewGuid(),
            ServerId = server.Id,
            NodeName = server.NodeName,
            Type = commandType,
            Status = "Pending",
            CreatedUtc = DateTime.UtcNow
        };

        db.ServerCommands.Add(command);
        await db.SaveChangesAsync(ct);

        await _statusNotifier.NotifyStatusChangedAsync(
            new ServerStatusChangedMessage(
                server.Id,
                server.Name,
                server.Status,
                server.ProcessId,
                server.UpdatedUtc),
            ct);
    }

    private static ServerSettingsDto BuildDefaultSettings(string serverName, string gameKey)
    {
        var settings = new ServerSettingsDto();

        if (gameKey.Equals("valheim", StringComparison.OrdinalIgnoreCase)
            || gameKey.Equals("valheim-dedicated", StringComparison.OrdinalIgnoreCase)
            || gameKey.Equals("steam-valheim", StringComparison.OrdinalIgnoreCase))
        {
            settings.Valheim.ServerName = serverName;
            settings.Valheim.WorldName = MakeSafeWorldName(serverName);
            settings.Valheim.Password = "changeit";
            settings.Valheim.Public = true;
            settings.Valheim.Crossplay = true;
            settings.Valheim.Port = 2456;
        }

        return settings;
    }

    private static string SerializeSettings(ServerSettingsDto settings) =>
        JsonSerializer.Serialize(settings, JsonOptions);

    private static ServerSettingsDto DeserializeSettings(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ServerSettingsDto();

        return JsonSerializer.Deserialize<ServerSettingsDto>(json, JsonOptions)
               ?? new ServerSettingsDto();
    }

    private static string NormalizeInstallPath(string? requestedPath, string gameKey, string serverName, Guid serverId)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
            return requestedPath.Trim();

        var safeGameKey = MakeSafeSegment(gameKey, "game");
        var safeServerName = MakeSafeSegment(serverName, "server");
        return Path.Combine(safeGameKey, $"{safeServerName}-{serverId:N}"[..20]);
    }

    private static string MakeSafeWorldName(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "Dedicated" : value.Trim();
        var chars = source.Where(char.IsLetterOrDigit).Take(32).ToArray();
        return chars.Length == 0 ? "Dedicated" : new string(chars);
    }

    private static string MakeSafeSegment(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var cleaned = InvalidPathCharsRegex.Replace(value.Trim(), "-");
        cleaned = cleaned.Replace(' ', '-');
        while (cleaned.Contains("--", StringComparison.Ordinal))
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);

        cleaned = cleaned.Trim('-', '.', ' ');
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }
}
