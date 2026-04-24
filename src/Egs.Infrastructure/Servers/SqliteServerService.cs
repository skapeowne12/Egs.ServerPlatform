using System.Text.Json;
using System.Text.Json.Nodes;
using Egs.Agent.Abstractions.Status;
using Egs.Application.Servers;
using Egs.Contracts.Servers;
using Egs.Domain.Servers;
using Egs.Infrastructure.Data;
using Egs.PluginSdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Egs.Infrastructure.Servers;

public sealed class SqliteServerService : IServerService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IServerStatusNotifier _statusNotifier;
    private readonly IPluginCatalog _pluginCatalog;
    private readonly IConfiguration _configuration;

    public SqliteServerService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IServerStatusNotifier statusNotifier,
        IPluginCatalog pluginCatalog,
        IConfiguration configuration)
    {
        _dbContextFactory = dbContextFactory;
        _statusNotifier = statusNotifier;
        _pluginCatalog = pluginCatalog;
        _configuration = configuration;
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

        var settings = DeserializeSettings(server.SettingsJson);

        var plugin = TryGetPlugin(server.GameKey);


        var port = ResolvePort(settings);
        var queryPort = ResolveQueryPort(server.GameKey, settings, port);

        return new ServerDetailsDto
        {
            Id = server.Id,
            Name = server.Name,
            GameKey = server.GameKey,
            NodeName = server.NodeName,
            InstallPath = server.InstallPath,
            Status = server.Status,
            ProcessId = server.ProcessId,
            IpAddress = ResolveIpAddress(server.NodeName, settings),
            Port = port,
            QueryPort = queryPort,
            StartedUtc = server.StartedUtc,
            PluginDisplayName = plugin?.DisplayName ?? server.GameKey,
            PluginMetadata = plugin is null
                ? []
                : BuildPluginMetadata(server, plugin, settings),
            Settings = settings
        };
    }
    private PluginDescriptor? TryGetPlugin(string gameKey)
    {
        try
        {
            return _pluginCatalog.GetRequired(gameKey);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
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
        var nodeName = string.IsNullOrWhiteSpace(request.NodeName) ? "Local" : request.NodeName.Trim();

        var plugin = _pluginCatalog.GetRequired(normalizedGameKey);
        var defaultSettings = BuildDefaultSettings(plugin);
        var installPath = NormalizeInstallPath(request.InstallPath, normalizedGameKey, normalizedName, nodeName, serverId, plugin, defaultSettings);

        var server = new ServerInstance
        {
            Id = serverId,
            Name = normalizedName,
            GameKey = normalizedGameKey,
            NodeName = nodeName,
            InstallPath = installPath,
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

    public Task UninstallAsync(Guid id, CancellationToken ct = default)
        => QueueCommandAsync(id, "Uninstall", "Uninstalling", ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => QueueCommandAsync(id, "Delete", "Deleting", ct);

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

    private static ServerSettingsDto BuildDefaultSettings(PluginDescriptor plugin) =>
        new()
        {
            Plugin = PluginCatalogJson.CloneObject(plugin.DefaultSettings)
        };

    private static string SerializeSettings(ServerSettingsDto settings) =>
        JsonSerializer.Serialize(settings, JsonOptions);

    private static ServerSettingsDto DeserializeSettings(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ServerSettingsDto();

        try
        {
            return JsonSerializer.Deserialize<ServerSettingsDto>(json, JsonOptions)
                   ?? new ServerSettingsDto();
        }
        catch
        {
            return new ServerSettingsDto();
        }
    }

    private string? ResolveIpAddress(string nodeName, ServerSettingsDto settings)
    {
        if (TryGetString(settings.Plugin, "ipAddress", out var pluginIp))
            return pluginIp;

        if (TryGetString(settings.Plugin, "publicIp", out var publicIp))
            return publicIp;

        var configured = _configuration[$"NodeAddresses:{nodeName}"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        if (string.Equals(nodeName, "Local", StringComparison.OrdinalIgnoreCase))
            return "127.0.0.1";

        return null;
    }

    private static int? ResolvePort(ServerSettingsDto settings)
    {
        if (TryGetInt(settings.Plugin, "port", out var port))
            return port;

        if (TryGetInt(settings.Plugin, "gamePort", out var gamePort))
            return gamePort;

        return null;
    }

    private static int? ResolveQueryPort(string gameKey, ServerSettingsDto settings, int? port)
    {
        if (TryGetInt(settings.Plugin, "queryPort", out var queryPort))
            return queryPort;

        if (TryGetInt(settings.Plugin, "query_port", out queryPort))
            return queryPort;

        if (string.Equals(gameKey, "valheim", StringComparison.OrdinalIgnoreCase) && port.HasValue)
            return port.Value + 1;

        return null;
    }

    private static bool TryGetString(JsonObject? json, string propertyName, out string value)
    {
        value = string.Empty;

        if (json is null || !json.TryGetPropertyValue(propertyName, out var node) || node is null)
            return false;

        try
        {
            var parsed = node.GetValue<string>();
            if (string.IsNullOrWhiteSpace(parsed))
                return false;

            value = parsed.Trim();
            return true;
        }
        catch
        {
            var raw = node.ToString();
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            value = raw.Trim().Trim('"');
            return true;
        }
    }

    private static bool TryGetInt(JsonObject? json, string propertyName, out int value)
    {
        value = default;

        if (json is null || !json.TryGetPropertyValue(propertyName, out var node) || node is null)
            return false;

        try
        {
            value = node.GetValue<int>();
            return true;
        }
        catch
        {
        }

        try
        {
            var stringValue = node.GetValue<string>();
            return int.TryParse(stringValue, out value);
        }
        catch
        {
        }

        return int.TryParse(node.ToString(), out value);
    }
    private List<ServerMetadataItemDto> BuildPluginMetadata(
    ServerInstance server,
    PluginDescriptor plugin,
    ServerSettingsDto settings)
    {
        if (plugin.Metadata is null || plugin.Metadata.Count == 0)
            return [];

        var context = new PluginRenderContext
        {
            Plugin = plugin,
            ServerId = server.Id,
            ServerName = server.Name,
            GameKey = server.GameKey,
            NodeName = server.NodeName,
            InstallPath = ResolveAbsoluteInstallDirectory(server.InstallPath),
            InstallDirectory = ResolveAbsoluteInstallDirectory(server.InstallPath),
            Settings = settings
        };

        var items = new List<ServerMetadataItemDto>();

        foreach (var definition in plugin.Metadata)
        {
            var value = ResolveMetadataValue(definition, context);

            if (string.IsNullOrWhiteSpace(value))
                continue;

            items.Add(new ServerMetadataItemDto
            {
                Key = definition.Key,
                Label = string.IsNullOrWhiteSpace(definition.Label) ? definition.Key : definition.Label,
                Value = value,
                Highlight = definition.Highlight
            });
        }

        return items;
    }

    private string ResolveMetadataValue(PluginMetadataDefinition definition, PluginRenderContext context)
    {
        if (string.Equals(definition.Type, "static", StringComparison.OrdinalIgnoreCase))
        {
            return PluginTemplateRenderer.Render(definition.Value ?? string.Empty, context).Trim();
        }

        if (!string.Equals(definition.Type, "jsonValue", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var renderedPath = PluginTemplateRenderer.Render(definition.Path ?? string.Empty, context);
        if (string.IsNullOrWhiteSpace(renderedPath))
            return string.Empty;

        var fullPath = renderedPath.Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(fullPath))
            fullPath = ResolveAbsoluteInstallDirectory(fullPath);

        if (!File.Exists(fullPath))
            return string.Empty;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(fullPath));
            if (root is null)
                return string.Empty;

            if (!TryReadSimpleJsonPath(root, definition.JsonPath ?? string.Empty, out var node) || node is null)
                return string.Empty;

            if (node is JsonValue jsonValue)
            {
                if (jsonValue.TryGetValue<bool>(out var boolValue))
                    return boolValue ? "Yes" : "No";

                if (jsonValue.TryGetValue<int>(out var intValue))
                    return intValue.ToString();

                if (jsonValue.TryGetValue<long>(out var longValue))
                    return longValue.ToString();

                if (jsonValue.TryGetValue<double>(out var doubleValue))
                    return doubleValue.ToString();

                if (jsonValue.TryGetValue<string>(out var stringValue))
                    return stringValue?.Trim() ?? string.Empty;
            }

            return node.ToJsonString().Trim('"');
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryReadSimpleJsonPath(JsonNode root, string jsonPath, out JsonNode? node)
    {
        node = root;

        if (string.IsNullOrWhiteSpace(jsonPath))
            return false;

        var path = jsonPath.Trim();

        if (path.StartsWith("$.", StringComparison.Ordinal))
            path = path[2..];
        else if (path.StartsWith("$", StringComparison.Ordinal))
            path = path[1..];

        if (string.IsNullOrWhiteSpace(path))
            return false;

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(segment, out node) || node is null)
                return false;
        }

        return true;
    }

    private string ResolveAbsoluteInstallDirectory(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return installPath;

        if (Path.IsPathRooted(installPath))
            return installPath;

        var root = _configuration["ServerInstallRoot"];
        if (string.IsNullOrWhiteSpace(root))
            root = @"C:\Egs\Servers";

        return Path.GetFullPath(Path.Combine(root, installPath));
    }

    private static string NormalizeInstallPath(
        string? requestedPath,
        string gameKey,
        string serverName,
        string nodeName,
        Guid serverId,
        PluginDescriptor plugin,
        ServerSettingsDto settings)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
            return requestedPath.Trim();

        if (!string.IsNullOrWhiteSpace(plugin.DefaultInstallPathTemplate))
        {
            var context = new PluginRenderContext
            {
                Plugin = plugin,
                ServerId = serverId,
                ServerName = serverName,
                GameKey = gameKey,
                NodeName = nodeName,
                InstallPath = string.Empty,
                InstallDirectory = string.Empty,
                Settings = settings
            };

            var rendered = PluginTemplateRenderer.Render(plugin.DefaultInstallPathTemplate, context);
            if (!string.IsNullOrWhiteSpace(rendered))
            {
                return rendered.Replace('/', Path.DirectorySeparatorChar);
            }
        }

        var safeGameKey = PluginTemplateRenderer.MakeSafePathSegment(gameKey, "game");
        var safeServerName = PluginTemplateRenderer.MakeSafePathSegment(serverName, "server");
        return Path.Combine(safeGameKey, $"{safeServerName}-{serverId:N}"[..20]);
    }
}