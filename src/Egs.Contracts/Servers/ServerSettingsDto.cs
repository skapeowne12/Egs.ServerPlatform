using System.Text.Json.Nodes;

namespace Egs.Contracts.Servers;

public sealed class ServerSettingsDto
{
    public string StartupArguments { get; set; } = string.Empty;
    public bool AutoStart { get; set; }
    public bool AutoRestart { get; set; }
    public bool AutoUpdate { get; set; }
    public DiscordBotSettingsDto DiscordBot { get; set; } = new();
    public CpuSettingsDto Cpu { get; set; } = new();
    public JsonObject Plugin { get; set; } = new JsonObject();
}
