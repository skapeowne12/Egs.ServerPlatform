namespace Egs.PluginSdk;

public sealed class PluginInstallerDefinition
{
    public string Type { get; set; } = string.Empty;
    public string SteamAppId { get; set; } = string.Empty;
    public string LoginMode { get; set; } = "anonymous-first";
    public bool ValidateOnInstall { get; set; } = true;
}
