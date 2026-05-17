using System.Text;
using System.Text.Json;
using Egs.Contracts.Cloud;
using Microsoft.Extensions.Options;

namespace Egs.Agent.Windows.Services;

public sealed class CloudControlClient
{
    private readonly HttpClient _httpClient;
    private readonly CloudControlOptions _options;
    private readonly CloudControlState _state;
    private readonly ILogger<CloudControlClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public CloudControlClient(
        HttpClient httpClient,
        IOptions<CloudControlOptions> options,
        CloudControlState state,
        ILogger<CloudControlClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _state = state;
        _logger = logger;
    }

    public async Task<EgsNodeDto?> SendHeartbeatAsync(CancellationToken ct)
    {
        var request = new UpsertNodeRequest(
            _options.NodeKey,
            _options.DisplayName,
            _options.OperatingSystem,
            Environment.MachineName);

        var json = JsonSerializer.Serialize(request, JsonOptions);

        _logger.LogInformation("Sending cloud heartbeat payload: {Payload}", json);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.PostAsync(
            "api/node/heartbeat",
            content,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Cloud heartbeat failed. StatusCode={StatusCode}, Body={Body}",
                response.StatusCode,
                body);

            response.EnsureSuccessStatusCode();
        }

        var node = JsonSerializer.Deserialize<EgsNodeDto>(body, JsonOptions);

        if (node is not null)
        {
            _state.NodeId = node.Id;
        }

        return node;
    }

    public async Task SyncServerAsync(UpsertCloudServerRequest request, CancellationToken ct)
    {
        if (_state.NodeId is null)
        {
            _logger.LogWarning("Skipping cloud server sync because node has not heartbeated yet.");
            return;
        }

        var json = JsonSerializer.Serialize(request, JsonOptions);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.PutAsync(
            $"api/node/{_state.NodeId.Value}/servers/{request.ServerId}",
            content,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Cloud server sync failed. ServerId={ServerId}, StatusCode={StatusCode}, Body={Body}",
                request.ServerId,
                response.StatusCode,
                body);

            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation(
            "Cloud server sync OK. ServerId={ServerId}, Name={Name}, Status={Status}",
            request.ServerId,
            request.Name,
            request.Status);
    }
    public async Task<IReadOnlyList<CloudServerCommandDto>> PollCommandsAsync(CancellationToken ct)
    {
        if (_state.NodeId is null)
        {
            _logger.LogWarning("Skipping cloud command poll because node has not heartbeated yet.");
            return Array.Empty<CloudServerCommandDto>();
        }

        using var response = await _httpClient.GetAsync(
            $"api/node/{_state.NodeId.Value}/commands",
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Cloud command poll failed. NodeId={NodeId}, StatusCode={StatusCode}, Body={Body}",
                _state.NodeId.Value,
                response.StatusCode,
                body);

            response.EnsureSuccessStatusCode();
        }

        return JsonSerializer.Deserialize<List<CloudServerCommandDto>>(body, JsonOptions)
            ?? new List<CloudServerCommandDto>();
    }

    public async Task UpdateCommandStatusAsync(
        Guid commandId,
        string status,
        string? resultJson,
        string? errorMessage,
        CancellationToken ct)
    {
        var request = new UpdateCloudCommandStatusRequest(
            status,
            resultJson,
            errorMessage);

        var json = JsonSerializer.Serialize(request, JsonOptions);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.PostAsync(
            $"api/node/commands/{commandId}/status",
            content,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Cloud command status update failed. CommandId={CommandId}, Status={Status}, StatusCode={StatusCode}, Body={Body}",
                commandId,
                status,
                response.StatusCode,
                body);

            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation(
            "Cloud command status updated. CommandId={CommandId}, Status={Status}",
            commandId,
            status);
    }

    public async Task PushServerStatusAsync(
        Guid serverId,
        PushCloudServerStatusRequest request,
        CancellationToken ct)
    {
        if (_state.NodeId is null)
        {
            _logger.LogWarning("Skipping cloud server status push because node has not heartbeated yet.");
            return;
        }

        var json = JsonSerializer.Serialize(request, JsonOptions);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.PostAsync(
            $"api/node/{_state.NodeId.Value}/servers/{serverId}/status",
            content,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Cloud server status push failed. ServerId={ServerId}, StatusCode={StatusCode}, Body={Body}",
                serverId,
                response.StatusCode,
                body);

            response.EnsureSuccessStatusCode();
        }

        _logger.LogInformation(
            "Cloud server status pushed. ServerId={ServerId}, Status={Status}",
            serverId,
            request.Status);
    }

    public async Task PushConsoleLinesAsync(
        Guid serverId,
        IEnumerable<PushCloudConsoleLineItem> lines,
        CancellationToken ct)
    {
        if (_state.NodeId is null)
        {
            _logger.LogWarning("Skipping cloud console push because node has not heartbeated yet.");
            return;
        }

        var lineList = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Line))
            .ToList();

        if (lineList.Count == 0)
        {
            return;
        }

        var request = new PushCloudConsoleLinesRequest(lineList);
        var json = JsonSerializer.Serialize(request, JsonOptions);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.PostAsync(
            $"api/node/{_state.NodeId.Value}/servers/{serverId}/console",
            content,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Cloud console push failed. ServerId={ServerId}, Count={Count}, StatusCode={StatusCode}, Body={Body}",
                serverId,
                lineList.Count,
                response.StatusCode,
                body);

            response.EnsureSuccessStatusCode();
        }

        _logger.LogDebug(
            "Cloud console lines pushed. ServerId={ServerId}, Count={Count}",
            serverId,
            lineList.Count);
    }
}

public sealed class CloudControlState
{
    public Guid? NodeId { get; set; }
}
