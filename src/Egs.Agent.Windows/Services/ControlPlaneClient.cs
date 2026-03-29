using System.Net.Http.Json;
using Egs.Agent.Abstractions.Commands;
using Egs.Agent.Abstractions.Console;
using Egs.Agent.Abstractions.Status;

namespace Egs.Agent.Windows.Services;

public sealed class ControlPlaneClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public ControlPlaneClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<ServerCommandMessage>> PollCommandsAsync(CancellationToken ct)
    {
        var nodeName = _configuration["Agent:NodeName"] ?? "Local";

        var response = await _httpClient.GetFromJsonAsync<AgentPollResponse>(
            $"api/agent/commands/{Uri.EscapeDataString(nodeName)}",
            ct);

        return response?.Commands ?? [];
    }

    public async Task PostStatusAsync(AgentServerUpdateMessage message, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync("api/agent/status", message, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task PostConsoleLineAsync(ConsoleLineMessage message, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync("api/agent/console", message, ct);
        response.EnsureSuccessStatusCode();
    }
}