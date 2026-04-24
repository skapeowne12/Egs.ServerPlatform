namespace Egs.PluginSdk;

public sealed class PluginSetupActionDefinition
{
    public string Stage { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string TemplateFile { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Search { get; set; } = string.Empty;
    public string Replace { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string JsonPath { get; set; } = string.Empty;
    public bool Overwrite { get; set; } = true;
    public bool CreateIfMissing { get; set; } = true;
    public bool IgnoreIfMissing { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string ArgumentsTemplate { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string WaitForPath { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 60;
}
