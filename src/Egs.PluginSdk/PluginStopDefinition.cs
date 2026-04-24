namespace Egs.PluginSdk;

public sealed class PluginStopDefinition
{
    public string Mode { get; set; } = "kill-process-tree";
    public int GracePeriodSeconds { get; set; } = 30;
}
