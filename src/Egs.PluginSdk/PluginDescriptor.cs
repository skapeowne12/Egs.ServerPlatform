using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Egs.PluginSdk;

public sealed class PluginDescriptor
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefaultInstallPathTemplate { get; set; } = string.Empty;
    public PluginInstallerDefinition Installer { get; set; } = new();
    public PluginRuntimeDefinition Runtime { get; set; } = new();
    public List<PluginRequirementDefinition> Requirements { get; set; } = [];
    public List<PluginSetupActionDefinition> Setup { get; set; } = [];
    public JsonObject DefaultSettings { get; set; } = new JsonObject();
    public JsonObject SettingsSchema { get; set; } = new JsonObject();
    public List<PluginMetadataDefinition> Metadata { get; set; } = [];

    [JsonIgnore]
    public string SourceDirectory { get; set; } = string.Empty;
}
