using Egs.Contracts.Servers;

namespace Egs.Agent.Abstractions.Servers;

public sealed class AgentServerDefinitionMessage
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GameKey { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ProcessId { get; set; }
    public ServerSettingsDto Settings { get; set; } = new();
}
