namespace Egs.Contracts.Servers;

public sealed class ValheimServerSettingsDto
{
    public string ServerName { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string Password { get; set; } = "changeit";
    public int Port { get; set; } = 2456;
    public bool Public { get; set; } = true;
    public bool Crossplay { get; set; } = true;
    public string SaveDirectory { get; set; } = string.Empty;
    public string LogFilePath { get; set; } = string.Empty;
    public int? SaveIntervalSeconds { get; set; }
    public int? BackupCount { get; set; }
    public int? BackupShortSeconds { get; set; }
    public int? BackupLongSeconds { get; set; }
    public string InstanceId { get; set; } = string.Empty;
    public string Preset { get; set; } = string.Empty;
}
