using System.Diagnostics;
using Egs.Agent.Abstractions.Servers;
using Egs.PluginSdk;

namespace Egs.Agent.Windows.Services.Runtimes;

public sealed class ManifestGameServerRuntime : IGameServerRuntime
{
    private readonly IPluginCatalog _pluginCatalog;
    private readonly SteamCmdService _steamCmdService;
    private readonly PluginSetupActionExecutor _setupActionExecutor;
    private readonly ILogger<ManifestGameServerRuntime> _logger;

    public ManifestGameServerRuntime(
        IPluginCatalog pluginCatalog,
        SteamCmdService steamCmdService,
        PluginSetupActionExecutor setupActionExecutor,
        ILogger<ManifestGameServerRuntime> logger)
    {
        _pluginCatalog = pluginCatalog;
        _steamCmdService = steamCmdService;
        _setupActionExecutor = setupActionExecutor;
        _logger = logger;
    }

    public bool CanHandle(string gameKey) => _pluginCatalog.TryGet(gameKey) is not null;

    public async Task InstallAsync(AgentServerDefinitionMessage server, Func<string, Task> writeLineAsync, CancellationToken ct)
    {
        var plugin = _pluginCatalog.GetRequired(server.GameKey);
        var installDirectory = ResolveInstallDirectory(server, plugin);
        var context = CreateContext(server, plugin, installDirectory);

        await writeLineAsync($"[Install] Resolved install directory: {installDirectory}");
        await _setupActionExecutor.ExecuteStageAsync(plugin, "beforeInstall", context, writeLineAsync, ct);

        switch (plugin.Installer.Type.Trim().ToLowerInvariant())
        {
            case "steamcmd":
                if (string.IsNullOrWhiteSpace(plugin.Installer.SteamAppId))
                {
                    throw new InvalidOperationException($"Plugin '{plugin.Key}' is missing Installer.SteamAppId.");
                }

                await _steamCmdService.InstallAppAsync(
                    plugin.Installer.SteamAppId,
                    installDirectory,
                    plugin.Installer.ValidateOnInstall,
                    string.Equals(plugin.Installer.LoginMode, "anonymous-first", StringComparison.OrdinalIgnoreCase),
                    writeLineAsync,
                    ct);
                break;

            default:
                throw new NotSupportedException($"Installer type '{plugin.Installer.Type}' is not supported.");
        }

        await _setupActionExecutor.ExecuteStageAsync(plugin, "afterInstall", context, writeLineAsync, ct);

        var executablePath = ResolveExecutablePath(plugin, context);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"Executable not found after install. Expected path: {executablePath}",
                executablePath);
        }

        await writeLineAsync($"[Install] Plugin '{plugin.DisplayName}' is installed and ready.");
    }

    public async Task<Process> StartAsync(AgentServerDefinitionMessage server, Func<string, Task> writeLineAsync, CancellationToken ct)
    {
        var plugin = _pluginCatalog.GetRequired(server.GameKey);
        var installDirectory = ResolveInstallDirectory(server, plugin);
        var context = CreateContext(server, plugin, installDirectory);

        await _setupActionExecutor.ExecuteStageAsync(plugin, "beforeStart", context, writeLineAsync, ct);

        var executablePath = ResolveExecutablePath(plugin, context);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"Executable not found. Run Install first. Expected path: {executablePath}",
                executablePath);
        }

        var workingDirectory = ResolveWorkingDirectory(plugin, context);
        var arguments = PluginTemplateRenderer.Render(plugin.Runtime.ArgumentsTemplate, context).Trim();
        var readinessPatterns = plugin.Runtime.Readiness.StdoutContains
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        TaskCompletionSource<bool>? readinessTcs = null;
        if (plugin.Runtime.Readiness.TimeoutSeconds > 0 && readinessPatterns.Count > 0)
        {
            readinessTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data))
            {
                return;
            }

            var line = args.Data!;
            _ = writeLineAsync($"[Process] {line}");

            if (readinessTcs is not null
                && readinessPatterns.Any(pattern => line.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                readinessTcs.TrySetResult(true);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data))
            {
                return;
            }

            _ = writeLineAsync($"[Process] {args.Data}");
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{executablePath}'.");
        }

        _logger.LogInformation("Started game server process {ProcessId}: {FileName} {Arguments}", process.Id, executablePath, arguments);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (readinessTcs is not null)
        {
            using var readinessCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readinessCts.CancelAfter(TimeSpan.FromSeconds(plugin.Runtime.Readiness.TimeoutSeconds));

            try
            {
                await readinessTcs.Task.WaitAsync(readinessCts.Token);
                await writeLineAsync($"[Start] Readiness signal detected for plugin '{plugin.Key}'.");
            }
            catch (OperationCanceledException)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException($"Process exited before reporting readiness for plugin '{plugin.Key}'.");
                }

                await writeLineAsync($"[Start] Readiness timed out after {plugin.Runtime.Readiness.TimeoutSeconds} seconds; continuing because the process is still running.");
            }
        }

        await _setupActionExecutor.ExecuteStageAsync(plugin, "afterStart", context, writeLineAsync, ct);
        return process;
    }

    public async Task StopAsync(AgentServerDefinitionMessage server, Process process, Func<string, Task> writeLineAsync, CancellationToken ct)
    {
        if (process.HasExited)
        {
            return;
        }

        var plugin = _pluginCatalog.GetRequired(server.GameKey);
        var mode = plugin.Runtime.Stop.Mode.Trim().ToLowerInvariant();
        var gracePeriod = TimeSpan.FromSeconds(plugin.Runtime.Stop.GracePeriodSeconds <= 0 ? 30 : plugin.Runtime.Stop.GracePeriodSeconds);

        switch (mode)
        {
            case "close-main-window-then-kill":
                await writeLineAsync($"[Stop] Attempting graceful CloseMainWindow() for PID {process.Id}.");
                if (process.CloseMainWindow())
                {
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    waitCts.CancelAfter(gracePeriod);

                    try
                    {
                        await process.WaitForExitAsync(waitCts.Token);
                        await writeLineAsync("[Stop] Process exited gracefully.");
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        await writeLineAsync("[Stop] Graceful stop timed out; killing process tree.");
                    }
                }
                else
                {
                    await writeLineAsync("[Stop] No main window was available; killing process tree.");
                }

                break;

            case "kill-process-tree":
                await writeLineAsync($"[Stop] Killing process tree for PID {process.Id}.");
                break;

            default:
                await writeLineAsync($"[Stop] Stop mode '{plugin.Runtime.Stop.Mode}' is not supported; killing process tree.");
                break;
        }

        process.Kill(entireProcessTree: true);
    }

    private string ResolveInstallDirectory(AgentServerDefinitionMessage server, PluginDescriptor plugin)
    {
        var installPath = server.InstallPath;
        if (string.IsNullOrWhiteSpace(installPath))
        {
            var context = new PluginRenderContext
            {
                Plugin = plugin,
                ServerId = server.Id,
                ServerName = server.Name,
                GameKey = server.GameKey,
                NodeName = server.NodeName,
                InstallPath = string.Empty,
                InstallDirectory = string.Empty,
                Settings = server.Settings
            };

            installPath = string.IsNullOrWhiteSpace(plugin.DefaultInstallPathTemplate)
                ? Path.Combine(
                    PluginTemplateRenderer.MakeSafePathSegment(server.GameKey, "game"),
                    PluginTemplateRenderer.MakeSafePathSegment(server.Name, "server"))
                : PluginTemplateRenderer.Render(plugin.DefaultInstallPathTemplate, context);
        }

        return _steamCmdService.ResolveInstallDirectory(installPath);
    }

    private static PluginRenderContext CreateContext(AgentServerDefinitionMessage server, PluginDescriptor plugin, string installDirectory) =>
        new()
        {
            Plugin = plugin,
            ServerId = server.Id,
            ServerName = server.Name,
            GameKey = server.GameKey,
            NodeName = server.NodeName,
            InstallPath = server.InstallPath,
            InstallDirectory = installDirectory,
            Settings = server.Settings
        };

    private static string ResolveExecutablePath(PluginDescriptor plugin, PluginRenderContext context)
    {
        var rendered = PluginTemplateRenderer.Render(plugin.Runtime.ExecutablePath, context);
        rendered = NormalizePath(rendered);

        if (Path.IsPathRooted(rendered))
        {
            return Path.GetFullPath(rendered);
        }

        return Path.GetFullPath(Path.Combine(context.InstallDirectory, rendered));
    }

    private static string ResolveWorkingDirectory(PluginDescriptor plugin, PluginRenderContext context)
    {
        var rendered = PluginTemplateRenderer.Render(plugin.Runtime.WorkingDirectory, context);
        rendered = NormalizePath(rendered);

        if (string.IsNullOrWhiteSpace(rendered))
        {
            return context.InstallDirectory;
        }

        if (Path.IsPathRooted(rendered))
        {
            return Path.GetFullPath(rendered);
        }

        return Path.GetFullPath(Path.Combine(context.InstallDirectory, rendered));
    }
    public async Task UninstallAsync(AgentServerDefinitionMessage server, Func<string, Task> writeLineAsync, CancellationToken ct)
    {
        var plugin = _pluginCatalog.GetRequired(server.GameKey);
        var installDirectory = ResolveInstallDirectory(server, plugin);

        await writeLineAsync($"[Uninstall] Resolved install directory: {installDirectory}");

        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            await writeLineAsync("[Uninstall] Install directory does not exist. Nothing to remove.");
            return;
        }

        var fullPath = Path.GetFullPath(installDirectory);
        var root = Path.GetPathRoot(fullPath);

        if (string.IsNullOrWhiteSpace(root) ||
            string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to uninstall dangerous path '{fullPath}'.");
        }

        Directory.Delete(fullPath, recursive: true);
        await writeLineAsync($"[Uninstall] Removed '{fullPath}'.");
    }

    private static string NormalizePath(string value) =>
        value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
}
