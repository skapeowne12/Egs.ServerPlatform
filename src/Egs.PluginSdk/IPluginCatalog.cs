namespace Egs.PluginSdk;

public interface IPluginCatalog
{
    IReadOnlyList<PluginDescriptor> GetAll();
    PluginDescriptor? TryGet(string key);
    PluginDescriptor GetRequired(string key);
}
