using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Egs.Contracts.Cloud;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Egs.Agent.Windows.Cloud;

public sealed class CloudControlClient
{
    private readonly HttpClient _httpClient;
    private readonly CloudControlOptions _options;
    private readonly ILogger<CloudControlClient> _logger;

    private Guid? _nodeId;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public CloudControlClient(
        HttpClient httpClient,
        IOptions<CloudControlOptions> options,
        ILogger<CloudControlClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
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
            _nodeId = node.Id;
        }

        return node;
    }

    public async Task SyncServerAsync(UpsertCloudServerRequest request, CancellationToken ct)
    {
        if (_nodeId is null)
        {
            _logger.LogDebug("Skipping cloud server sync because node has not heartbeated yet.");
            return;
        }

        var json = JsonSerializer.Serialize(request, JsonOptions);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.PutAsync(
            $"api/node/{_nodeId.Value}/servers/{request.ServerId}",
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
}