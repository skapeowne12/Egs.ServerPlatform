using System.Diagnostics;
using Egs.Agent.Abstractions.Servers;

namespace Egs.Agent.Windows.Services.Runtimes;

public abstract class SteamCmdGameRuntime : IGameServerRuntime
{
    private readonly SteamCmdService _steamCmdService;
    private readonly ILogger<SteamCmdGameRuntime> _logger;

    protected SteamCmdGameRuntime(
        SteamCmdService steamCmdService,
        ILogger<SteamCmdGameRuntime> logger)
    {
        _steamCmdService = steamCmdService;
        _logger = logger;
    }

    protected abstract IReadOnlyCollection<string> SupportedGameKeys { get; }
    protected abstract string SteamAppId { get; }
    protected abstract string ExecutableRelativePath { get; }
    protected virtual TimeSpan? StartupReadinessTimeout => null;

    public bool CanHandle(string gameKey) =>
        SupportedGameKeys.Contains(gameKey, StringComparer.OrdinalIgnoreCase);

    public async Task InstallAsync(AgentServerDefinitionMessage server, Func<string, Task> writeLineAsync, CancellationToken ct)
    {
        var installDirectory = _steamCmdService.ResolveInstallDirectory(server.InstallPath);
        await writeLineAsync($"[Install] Resolved install directory: {installDirectory}");
        await _steamCmdService.InstallAppAsync(SteamAppId, installDirectory, validate: true, writeLineAsync, ct);

        var executablePath = GetExecutablePath(server);
        await ValidateInstallAsync(server, executablePath, writeLineAsync, ct);
    }

    public async Task<Process> StartAsync(AgentServerDefinitionMessage server, Func<string, Task> writeLineAsync, CancellationToken ct)
    {
        var executablePath = GetExecutablePath(server);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"Executable not found. Run Install first. Expected path: {executablePath}",
                executablePath);
        }

        var arguments = BuildStartArguments(server);
        var workingDirectory = GetWorkingDirectory(server, executablePath);
        var readinessTcs = StartupReadinessTimeout is null
            ? null
            : new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                SignalReadiness(args.Data, readinessTcs);
                _ = writeLineAsync($"[Game] {args.Data}");
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                SignalReadiness(args.Data, readinessTcs);
                _ = writeLineAsync($"[Game] {args.Data}");
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start executable '{executablePath}'.");
        }

        _logger.LogInformation(
            "Started game server process {ProcessId}: {ExecutablePath} {Arguments}",
            process.Id,
            executablePath,
            arguments);

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WaitForStartupReadinessAsync(process, readinessTcs, ct);

        return process;
    }

    public virtual async Task StopAsync(AgentServerDefinitionMessage server, Process process, Func<string, Task> writeLineAsync, CancellationToken ct)
    {
        if (process.HasExited)
        {
            return;
        }

        await writeLineAsync($"[Stop] Terminating process tree for PID {process.Id}.");
        process.Kill(entireProcessTree: true);
    }

    protected virtual string GetWorkingDirectory(AgentServerDefinitionMessage server, string executablePath) =>
        Path.GetDirectoryName(executablePath) ?? _steamCmdService.ResolveInstallDirectory(server.InstallPath);

    protected abstract string BuildStartArguments(AgentServerDefinitionMessage server);

    protected virtual async Task ValidateInstallAsync(
        AgentServerDefinitionMessage server,
        string executablePath,
        Func<string, Task> writeLineAsync,
        CancellationToken ct)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                $"The runtime finished installing, but the expected executable was not found: {executablePath}",
                executablePath);
        }

        await writeLineAsync($"[Install] Runtime validation passed: {executablePath}");
    }

    protected virtual bool IsServerReadyOutput(string line) => false;

    private async Task WaitForStartupReadinessAsync(Process process, TaskCompletionSource<bool>? readinessTcs, CancellationToken ct)
    {
        if (readinessTcs is null)
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(StartupReadinessTimeout!.Value);

        var waitForExitTask = process.WaitForExitAsync(timeoutCts.Token);
        var completed = await Task.WhenAny(readinessTcs.Task, waitForExitTask);

        if (completed == readinessTcs.Task)
        {
            await readinessTcs.Task;
            return;
        }

        if (process.HasExited)
        {
            throw new InvalidOperationException($"Server process exited before reporting ready. Exit code: {process.ExitCode}.");
        }

        throw new TimeoutException($"Server did not report ready within {StartupReadinessTimeout.Value.TotalSeconds:0} seconds.");
    }

    private void SignalReadiness(string line, TaskCompletionSource<bool>? readinessTcs)
    {
        if (readinessTcs is null)
        {
            return;
        }

        if (IsServerReadyOutput(line))
        {
            readinessTcs.TrySetResult(true);
        }
    }

    private string GetExecutablePath(AgentServerDefinitionMessage server)
    {
        var installDirectory = _steamCmdService.ResolveInstallDirectory(server.InstallPath);
        return Path.Combine(installDirectory, ExecutableRelativePath);
    }
}
