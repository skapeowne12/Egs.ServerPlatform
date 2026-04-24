namespace Egs.PluginSdk;

public sealed class PluginRuntimeDefinition
{
    public string ExecutablePath { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = "{server.installDirectory}";
    public string ArgumentsTemplate { get; set; } = string.Empty;
    public PluginReadinessDefinition Readiness { get; set; } = new();
    public PluginStopDefinition Stop { get; set; } = new();
}
