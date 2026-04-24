using Egs.PluginSdk;
using Microsoft.AspNetCore.Mvc;

namespace Egs.Api.Controllers;

[ApiController]
[Route("api/plugins")]
public sealed class PluginsController : ControllerBase
{
    private readonly IPluginCatalog _pluginCatalog;

    public PluginsController(IPluginCatalog pluginCatalog)
    {
        _pluginCatalog = pluginCatalog;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        var plugins = _pluginCatalog
            .GetAll()
            .Select(x => new
            {
                x.Key,
                x.DisplayName,
                x.Description,
                InstallerType = x.Installer.Type,
                x.Requirements
            });

        return Ok(plugins);
    }

    [HttpGet("{key}")]
    public IActionResult GetByKey(string key)
    {
        var plugin = _pluginCatalog.TryGet(key);
        return plugin is null ? NotFound() : Ok(plugin);
    }
}
